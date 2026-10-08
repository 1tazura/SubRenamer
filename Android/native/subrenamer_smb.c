/* SubRenamer's narrow native ABI. libsmb2 remains a separate LGPL-2.1 library. */
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <sys/socket.h>
#include <time.h>
#include <smb2/smb2.h>
#include <smb2/libsmb2.h>
#include <smb2/libsmb2-raw.h>

struct sr_connection { struct smb2_context *smb; };
struct sr_reply { int done; int result; int compound; uint32_t volume; struct smb2fh *fh; };
struct sr_stat { uint64_t ino, birth, birth_ns, size; uint32_t type, attributes; };
static void stat_copy(struct sr_stat *out, const struct smb2_stat_64 *s) {
    out->ino=s->smb2_ino; out->birth=s->smb2_btime; out->birth_ns=s->smb2_btime_nsec;
    out->size=s->smb2_size; out->type=s->smb2_type; out->attributes=s->smb2_attributes;
}
void *sr_new(void) {
    struct sr_connection *c=calloc(1,sizeof(*c)); if (!c) return NULL;
    c->smb=smb2_init_context(); if (!c->smb) { free(c); return NULL; }
    smb2_set_timeout(c->smb,10); smb2_set_authentication(c->smb,SMB2_SEC_NTLMSSP);
    return c;
}
void sr_destroy(struct sr_connection *c) {
    if (!c) return;
    if (c->smb) smb2_destroy_context(c->smb);
    free(c);
}
int sr_connect(struct sr_connection *c,const char *host,const char *share,const char *user,const char *password) {
    if (!c || !c->smb) return -ENOTCONN;
    smb2_set_user(c->smb,user);
    /* NULL password deliberately selects a true anonymous NTLM session. */
    if (password) smb2_set_password(c->smb,password);
    return smb2_connect_share(c->smb,host,share,user);
}
int sr_guid(struct sr_connection *c,uint8_t *guid) {
    if (!c || !c->smb) return -ENOTCONN;
    memcpy(guid,smb2_get_server_guid(c->smb),16); return 0;
}
int sr_stat_path(struct sr_connection *c,const char *path,struct sr_stat *out) {
    struct smb2_stat_64 s;
    if (!c || !c->smb) return -ENOTCONN;
    int r=smb2_stat(c->smb,path,&s); if (!r) stat_copy(out,&s); return r;
}
void *sr_list(struct sr_connection *c,const char *path) {
    return c && c->smb ? smb2_opendir(c->smb,path) : NULL;
}
const char *sr_next(struct sr_connection *c,struct smb2dir *d,struct sr_stat *out) {
    if (!c || !c->smb) return NULL;
    struct smb2dirent *e=smb2_readdir(c->smb,d); if (!e) return NULL;
    stat_copy(out,&e->st); return e->name;
}
void sr_list_close(struct sr_connection *c,struct smb2dir *d) {
    if (c && c->smb && d) smb2_closedir(c->smb,d);
}
static void reply_cb(struct smb2_context *smb,int status,void *data,void *opaque) {
    (void)smb; (void)data;
    struct sr_reply *r=opaque;
    if (!r->result) r->result=status ? -nterror_to_errno((uint32_t)status) : 0;
    r->done=1;
}
static void create_cb(struct smb2_context *smb,int status,void *data,void *opaque) {
    struct sr_reply *r=opaque;
    if (!status) {
        struct smb2_create_reply *rep=data;
        r->fh=smb2_fh_from_file_id(smb,&rep->file_id);
        if (!r->fh) r->result=-ENOMEM;
    } else r->result=-nterror_to_errno((uint32_t)status);
    if (!r->compound) r->done=1;
}
static int await_reply(struct sr_connection *c,struct sr_reply *r) {
    time_t deadline=time(NULL)+15;
    while (!r->done) {
        struct pollfd p={.fd=smb2_get_fd(c->smb),.events=smb2_which_events(c->smb)};
        int n=poll(&p,1,1000);
        if (n<0 && errno==EINTR) continue;
        if (n<0 || time(NULL)>=deadline || smb2_service(c->smb,p.revents)<0) {
            /* Destroy pending callbacks before returning their stack storage.
             * Any owned delete-pending file is cleaned up by the server. */
            smb2_destroy_context(c->smb); c->smb=NULL; return -ENOTCONN;
        }
    }
    return r->result;
}
static void volume_cb(struct smb2_context *smb,int status,void *data,void *opaque) {
    struct sr_reply *r=opaque;
    if (!status) {
        struct smb2_query_info_reply *rep=data;
        struct smb2_file_fs_volume_info *volume=rep->output_buffer;
        if (volume) r->volume=volume->volume_serial_number;
        else r->result=-EIO;
        if (volume) smb2_free_data(smb,volume);
    }
    reply_cb(smb,status,data,opaque);
}
int sr_volume(struct sr_connection *c,uint32_t *serial) {
    if (!c || !c->smb) return -ENOTCONN;
    struct smb2fh *fh=smb2_open(c->smb,"",O_RDONLY|O_DIRECTORY);
    if (!fh) return -EIO;
    struct smb2_query_info_request req={0}; struct sr_reply r={0};
    req.info_type=SMB2_0_INFO_FILESYSTEM; req.file_info_class=SMB2_FILE_FS_VOLUME_INFORMATION;
    req.output_buffer_length=1024; memcpy(req.file_id,*smb2_get_file_id(fh),SMB2_FD_SIZE);
    struct smb2_pdu *p=smb2_cmd_query_info_async(c->smb,&req,volume_cb,&r);
    if (!p) { smb2_close(c->smb,fh); return -EIO; }
    smb2_queue_pdu(c->smb,p); int rc=await_reply(c,&r);
    if (c->smb) smb2_close(c->smb,fh);
    if (!rc) *serial=r.volume;
    return rc;
}
/* mode: 0 read, 1 exclusive create with rollback-on-close, 2 locked SHA undo.
 * No FILE_OPEN_IF, OVERWRITE, truncation, or path-based deletion is exposed. */
int sr_open(struct sr_connection *c,const char *path,int mode,void **handle) {
    if (!c || !c->smb) return -ENOTCONN;
    struct smb2_create_request req={0}; struct sr_reply r={0};
    req.impersonation_level=SMB2_IMPERSONATION_IMPERSONATION;
    req.desired_access=SMB2_FILE_READ_DATA|SMB2_FILE_READ_ATTRIBUTES;
    if (mode==1) req.desired_access|=SMB2_FILE_WRITE_DATA|SMB2_DELETE;
    if (mode==2) req.desired_access|=SMB2_DELETE;
    req.share_access=mode==0 ? SMB2_FILE_SHARE_READ : 0;
    req.create_disposition=mode==1 ? SMB2_FILE_CREATE : SMB2_FILE_OPEN;
    req.create_options=SMB2_FILE_NON_DIRECTORY_FILE|SMB2_FILE_OPEN_REPARSE_POINT;
    req.name=path;
    r.compound=mode==1;
    struct smb2_pdu *p=smb2_cmd_create_async(c->smb,&req,create_cb,&r);
    if (!p) return -EIO;
    if (mode==1) {
        /* FILE_DELETE_ON_CLOSE in CREATE is sticky on Samba and cannot be
         * cleared by commit. Instead compound FILE_CREATE + SET_INFO pending
         * deletion, which is clearable and is applied before returning a handle. */
        struct smb2_set_info_request set={0};
        struct smb2_file_disposition_info info={.delete_pending=1};
        set.info_type=SMB2_0_INFO_FILE; set.file_info_class=SMB2_FILE_DISPOSITION_INFORMATION;
        memcpy(set.file_id,compound_file_id,SMB2_FD_SIZE); set.input_data=&info;
        struct smb2_pdu *next=smb2_cmd_set_info_async(c->smb,&set,reply_cb,&r);
        if (!next) { smb2_free_pdu(c->smb,p); return -EIO; }
        smb2_add_compound_pdu(c->smb,p,next);
    }
    smb2_queue_pdu(c->smb,p);
    int rc=await_reply(c,&r);
    if (!rc) {
        struct smb2_stat_64 s;
        rc=smb2_fstat(c->smb,r.fh,&s);
        if (!rc && (s.smb2_type!=SMB2_TYPE_FILE || (s.smb2_attributes&SMB2_FILE_ATTRIBUTE_REPARSE_POINT))) rc=-ELOOP;
        if (rc) { smb2_close(c->smb,r.fh); return rc; }
        *handle=r.fh;
    }
    if (rc && r.fh && c->smb) smb2_close(c->smb,r.fh);
    return rc;
}
int sr_stat_handle(struct sr_connection *c,struct smb2fh *f,struct sr_stat *out) {
    if (!c || !c->smb) return -ENOTCONN;
    struct smb2_stat_64 s; int rc=smb2_fstat(c->smb,f,&s);
    if (!rc) stat_copy(out,&s);
    return rc;
}
int sr_read(struct sr_connection *c,struct smb2fh *f,uint8_t *b,uint32_t count,uint64_t offset) {
    if (!c || !c->smb) return -ENOTCONN;
    uint32_t max=smb2_get_max_read_size(c->smb); if (count>max) count=max;
    return smb2_pread(c->smb,f,b,count,offset);
}
int sr_write(struct sr_connection *c,struct smb2fh *f,const uint8_t *b,uint32_t count,uint64_t offset) {
    if (!c || !c->smb) return -ENOTCONN;
    uint32_t max=smb2_get_max_write_size(c->smb); if (count>max) count=max;
    return smb2_pwrite(c->smb,f,b,count,offset);
}
int sr_flush(struct sr_connection *c,struct smb2fh *f) {
    return c && c->smb ? smb2_fsync(c->smb,f) : -ENOTCONN;
}
int sr_disposition(struct sr_connection *c,struct smb2fh *f,int delete_pending) {
    if (!c || !c->smb) return -ENOTCONN;
    struct smb2_set_info_request req={0}; struct sr_reply r={0};
    struct smb2_file_disposition_info info={.delete_pending=delete_pending ? 1 : 0};
    req.info_type=SMB2_0_INFO_FILE; req.file_info_class=SMB2_FILE_DISPOSITION_INFORMATION;
    memcpy(req.file_id,*smb2_get_file_id(f),SMB2_FD_SIZE); req.input_data=&info;
    struct smb2_pdu *p=smb2_cmd_set_info_async(c->smb,&req,reply_cb,&r); if (!p) return -EIO;
    smb2_queue_pdu(c->smb,p); return await_reply(c,&r);
}
int sr_close(struct sr_connection *c,struct smb2fh *f) {
    return c && c->smb ? smb2_close(c->smb,f) : -ENOTCONN;
}
/* Test fault injection: normal app code does not call this. */
void sr_break_connection(struct sr_connection *c) {
    if (c && c->smb) shutdown(smb2_get_fd(c->smb),SHUT_RDWR);
}
