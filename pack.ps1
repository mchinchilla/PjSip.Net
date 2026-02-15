# Pack all publishable NuGet packages
# Native packages first, then Interop, then the top-level SDK

$config = "Release"
$output = "artifacts"

$projects = @(
    "src/PjSip.Net.Native.Win64/PjSip.Net.Native.Win64.csproj",
    "src/PjSip.Net.Native.MacOS/PjSip.Net.Native.MacOS.csproj",
    "src/PjSip.Net.Native.Android/PjSip.Net.Native.Android.csproj",
    "src/PjSip.Net.Native.iOS/PjSip.Net.Native.iOS.csproj",
    "src/PjSip.Net.Interop/PjSip.Net.Interop.csproj",
    "src/PjSip.Net/PjSip.Net.csproj"
)

foreach ($project in $projects) {
    dotnet pack $project -c $config -o $output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
