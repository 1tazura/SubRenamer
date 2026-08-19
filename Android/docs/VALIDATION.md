# Validation

The Android port is added to the fork without modifying the original `SubRenamer.Core` matching implementation.

CI validates three layers:

1. `dotnet test SubRenamer.Tests/SubRenamer.Tests.csproj -c Release` — original project tests.
2. `dotnet test Android/tests/SubRenamer.Mobile.Tests/SubRenamer.Mobile.Tests.csproj -c Release` — Android-shell integration tests, including the typed Core bridge.
3. `dotnet build Android/src/SubRenamer.Mobile.Android/SubRenamer.Mobile.Android.csproj -c Debug -f net10.0-android -t:SignAndroidPackage` — produces a debug-signed APK artifact.

The port preserves the torrent-safe invariant: video files are read-only inputs and matched subtitle bytes are written directly beside those existing videos.
