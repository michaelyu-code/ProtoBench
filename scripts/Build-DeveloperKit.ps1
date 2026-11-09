<#
.SYNOPSIS
Builds a shareable plugin SDK kit, standalone starters, sample packages and optional test app.
.EXAMPLE
./scripts/Build-DeveloperKit.ps1
.EXAMPLE
./scripts/Build-DeveloperKit.ps1 -OutputDirectory C:/Share/ToolkitSDK -SkipApp -NoZip
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/developer-kit'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SelfContainedApp,
    [ValidatePattern('^$|^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version = '',
    [switch]$SkipApp,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Build this kit on Windows (WinUI/x64).' }
$null = Get-Command dotnet -ErrorAction Stop
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$zipDestination = $destination.TrimEnd('\', '/') + '.zip'
if (Test-Path -LiteralPath $destination) { throw 'OutputDirectory already exists. Choose a new output folder; existing files will not be overwritten.' }
if (-not $NoZip -and (Test-Path -LiteralPath $zipDestination)) { throw 'The output ZIP already exists. Choose a new output folder.' }
$parent = [IO.Path]::GetDirectoryName($destination)
if ([string]::IsNullOrEmpty($parent)) { throw 'OutputDirectory must be a child folder, not a filesystem root.' }
$null = New-Item -ItemType Directory -Path $parent -Force
$staging = Join-Path $parent ('.developer-kit-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $staging
$temporaryZip = Join-Path $parent ('.developer-kit-' + [guid]::NewGuid().ToString('N') + '.zip')

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed (exit $LASTEXITCODE)." }
}
function Write-Utf8([string]$RelativePath, [string]$Content) {
    $path = Join-Path $staging $RelativePath
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
}

Push-Location -LiteralPath $repository
try {
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
    [xml]$winuiProject = Get-Content -LiteralPath (Join-Path $repository 'src/NonStopToolkit.PluginSdk.WinUI/NonStopToolkit.PluginSdk.WinUI.csproj') -Raw
    $framework = [string]$properties.Project.PropertyGroup.TargetFramework
    if ($framework -notmatch '^net(\d+)\.(\d+)$') { throw 'Unsupported portable target framework.' }
    $minimumSdk = $Matches[1] + '.' + $Matches[2] + '.100'
    $windowsFramework = [string]$winuiProject.Project.PropertyGroup.TargetFramework
    $minimumWindows = [string]$winuiProject.Project.PropertyGroup.TargetPlatformMinVersion
    $windowsAppSdk = [string]$winuiProject.SelectSingleNode("//PackageReference[@Include='Microsoft.WindowsAppSDK']").Version
    $sdkUsed = (& dotnet --version)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read .NET SDK version.' }

    $contractNames = @('NonStopToolkit.Core', 'NonStopToolkit.Parsing', 'NonStopToolkit.Generation', 'NonStopToolkit.Utilities', 'NonStopToolkit.PluginSdk')
    if (-not $SkipApp) {
        Write-Host 'Building SDKs and publishing the Windows x64 test app…'
        $publishArguments = @('publish', 'src/NonStopToolkit.Desktop/NonStopToolkit.Desktop.csproj', '-c', $Configuration,
            '-r', 'win-x64', '--self-contained', $SelfContainedApp.IsPresent.ToString().ToLowerInvariant(), '-p:Platform=x64',
            '-p:UpdatesEnabled=false', '-o', (Join-Path $staging 'app'), '--nologo')
        if ($Version) { $publishArguments += @("-p:Version=$Version", '-p:AssemblyVersion=1.0.0.0') }
        Invoke-DotNet -Arguments $publishArguments
        # Unpackaged WinUI publish can omit compiled XAML resources even though the build output runs.
        # Include them explicitly so the standalone kit app can initialize App.xaml and its views.
        $desktopOutputArguments = @('msbuild', 'src/NonStopToolkit.Desktop/NonStopToolkit.Desktop.csproj', '-nologo',
            '-p:Platform=x64', "-p:Configuration=$Configuration", '-p:RuntimeIdentifier=win-x64', '-getProperty:TargetDir')
        $desktopOutput = (& dotnet @desktopOutputArguments)
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the desktop build output.' }
        $desktopOutput = ([string]$desktopOutput).Trim()
        $desktopResources = @(Get-ChildItem -LiteralPath $desktopOutput -Filter '*.xbf' -File -Recurse)
        $desktopPri = Join-Path $desktopOutput 'ProtoBench.pri'
        if ($desktopResources.Count -eq 0 -or -not (Test-Path -LiteralPath $desktopPri -PathType Leaf)) { throw 'Compiled desktop XAML resources are missing.' }
        foreach ($resource in $desktopResources + @(Get-Item -LiteralPath $desktopPri)) {
            $resourceTarget = Join-Path (Join-Path $staging 'app') ([IO.Path]::GetRelativePath($desktopOutput, $resource.FullName))
            $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($resourceTarget)) -Force
            Copy-Item -LiteralPath $resource.FullName -Destination $resourceTarget
        }
        # Use exactly the host's DLLs so the distributed SDK and test app cannot drift.
        $null = New-Item -ItemType Directory -Path (Join-Path $staging 'sdk/net10.0'), (Join-Path $staging 'sdk/winui') -Force
        foreach ($name in $contractNames + @('NonStopToolkit.PluginSdk.WinUI')) {
            $sdkTarget = if ($name -eq 'NonStopToolkit.PluginSdk.WinUI') { 'sdk/winui' } else { 'sdk/net10.0' }
            foreach ($extension in @('.dll', '.pdb', '.xml', '.pri')) {
                $sourceFile = Join-Path $staging ('app/' + $name + $extension)
                if (Test-Path -LiteralPath $sourceFile -PathType Leaf) { Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $staging $sdkTarget) }
            }
        }
    }
    else {
        Write-Host 'Building portable plugin SDK and dependencies…'
        Invoke-DotNet -Arguments @('build', 'src/NonStopToolkit.PluginSdk/NonStopToolkit.PluginSdk.csproj', '-c', $Configuration, '-o', (Join-Path $staging 'sdk/net10.0'), '--nologo')
        Write-Host 'Building optional WinUI plugin SDK…'
        Invoke-DotNet -Arguments @('build', 'src/NonStopToolkit.PluginSdk.WinUI/NonStopToolkit.PluginSdk.WinUI.csproj', '-c', $Configuration, '-p:Platform=x64', '-o', (Join-Path $staging 'sdk/winui'), '--nologo')
        # WinUI runtime/reference packages come from the developer project's PackageReference.
        Get-ChildItem -LiteralPath (Join-Path $staging 'sdk/winui') -File |
            Where-Object Name -NotLike 'NonStopToolkit.PluginSdk.WinUI.*' | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    }
    $references = foreach ($name in $contractNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging "sdk/net10.0/$name.dll"))) { throw "Missing SDK dependency: $name" }
        '    <Reference Include="' + $name + '"><HintPath>$(MSBuildThisFileDirectory)net10.0/' + $name + '.dll</HintPath><Private>false</Private></Reference>'
    }
    Write-Utf8 'sdk/NonStopToolkit.PluginSdk.props' ("<Project>`n  <ItemGroup>`n" + ($references -join "`n") + "`n  </ItemGroup>`n</Project>`n")
    Write-Utf8 'sdk/NonStopToolkit.PluginSdk.WinUI.props' @'
<Project>
  <Import Project="NonStopToolkit.PluginSdk.props" />
  <ItemGroup>
    <Reference Include="NonStopToolkit.PluginSdk.WinUI">
      <HintPath>$(MSBuildThisFileDirectory)winui/NonStopToolkit.PluginSdk.WinUI.dll</HintPath><Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
'@
    Write-Utf8 'Directory.Build.props' '<Project><PropertyGroup><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>'
    Write-Utf8 'global.json' (@{ sdk = @{ version = $minimumSdk; rollForward = 'latestFeature' } } | ConvertTo-Json -Depth 4)
    Write-Utf8 '.gitignore' "**/bin/`n**/obj/`npackages/`n"

    $standard = Join-Path $staging 'samples/ExamplePlugin'
    $native = Join-Path $staging 'samples/ExampleWinUIPlugin'
    $null = New-Item -ItemType Directory -Path $standard, $native -Force
    Copy-Item -LiteralPath (Join-Path $repository 'samples/plugins/ExamplePlugin/ExamplePlugin.cs') -Destination $standard
    Copy-Item -LiteralPath (Join-Path $repository 'samples/plugins/ExamplePlugin/plugin.json') -Destination $standard
    Copy-Item -LiteralPath (Join-Path $repository 'samples/plugins/ExamplePlugin/definitions') -Destination $standard -Recurse
    Copy-Item -LiteralPath (Join-Path $repository 'samples/plugins/WinUIExamplePlugin/Plugin.cs') -Destination $native
    Copy-Item -LiteralPath (Join-Path $repository 'samples/plugins/WinUIExamplePlugin/plugin.json') -Destination $native
    $standardProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>__FRAMEWORK__</TargetFramework><EnableDynamicLoading>true</EnableDynamicLoading></PropertyGroup>
  <Import Project="../../sdk/NonStopToolkit.PluginSdk.props" />
  <ItemGroup>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="definitions/**/*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@
    Write-Utf8 'samples/ExamplePlugin/ExamplePlugin.csproj' ($standardProject.Replace('__FRAMEWORK__', $framework))
    $nativeProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>__WINDOWS_FRAMEWORK__</TargetFramework><TargetPlatformMinVersion>__MINIMUM_WINDOWS__</TargetPlatformMinVersion>
    <UseWinUI>true</UseWinUI><Platforms>x64</Platforms><PlatformTarget>x64</PlatformTarget>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <Import Project="../../sdk/NonStopToolkit.PluginSdk.WinUI.props" />
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="__WINDOWS_APP_SDK__" />
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@
    Write-Utf8 'samples/ExampleWinUIPlugin/ExampleWinUIPlugin.csproj' ($nativeProject.Replace('__WINDOWS_FRAMEWORK__', $windowsFramework).Replace('__MINIMUM_WINDOWS__', $minimumWindows).Replace('__WINDOWS_APP_SDK__', $windowsAppSdk))
    $null = New-Item -ItemType Directory -Path (Join-Path $staging 'docs'), (Join-Path $staging 'tools') -Force
    Copy-Item -LiteralPath (Join-Path $repository 'docs/PLUGINS.md') -Destination (Join-Path $staging 'docs')
    Copy-Item -LiteralPath (Join-Path $repository 'scripts/Build-PluginPackage.ps1') -Destination (Join-Path $staging 'tools')
    Copy-Item -LiteralPath (Join-Path $repository 'scripts/developer-kit/GETTING-STARTED.md') -Destination (Join-Path $staging 'README.md')

    # Include the contract source as a browsable API reference, without requiring the app source to build.
    foreach ($source in @(
        'PluginSdk/PluginContracts.cs', 'PluginSdk.WinUI/ICustomToolViewProvider.cs',
        'Parsing/Abstractions/ILogParser.cs', 'Parsing/Abstractions/IStreamingLogParser.cs', 'Parsing/Abstractions/IFieldDecoder.cs',
        'Generation/Abstractions/IFileGenerator.cs', 'Core/Fields/ParsedField.cs',
        'Core/Documents/ParseResult.cs', 'Core/Diagnostics/ParseDiagnostic.cs'
    )) {
        $target = Join-Path $staging ('contracts/' + $source)
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath (Join-Path $repository ('src/NonStopToolkit.' + $source)) -Destination $target
    }

    Write-Host 'Verifying standalone starters and creating example plugin packages…'
    # Run in a child PowerShell so a packaging failure cannot be hidden by an unrelated successful command.
    $powershellExecutable = (Get-Process -Id $PID).Path
    foreach ($project in @('samples/ExamplePlugin/ExamplePlugin.csproj', 'samples/ExampleWinUIPlugin/ExampleWinUIPlugin.csproj')) {
        & $powershellExecutable -NoProfile -File (Join-Path $staging 'tools/Build-PluginPackage.ps1') -ProjectPath (Join-Path $staging $project) -Configuration $Configuration | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Standalone starter failed: $project" }
    }
    # A complete app carries its own host contracts. Check those against the DLLs developers compile against.
    if (-not $SkipApp) {
        foreach ($name in $contractNames) {
            if ((Get-FileHash -LiteralPath (Join-Path $staging "sdk/net10.0/$name.dll")).Hash -ne
                (Get-FileHash -LiteralPath (Join-Path $staging "app/$name.dll")).Hash) { throw "SDK/app binary mismatch: $name" }
        }
        if ((Get-FileHash -LiteralPath (Join-Path $staging 'sdk/winui/NonStopToolkit.PluginSdk.WinUI.dll')).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $staging 'app/NonStopToolkit.PluginSdk.WinUI.dll')).Hash) { throw 'WinUI SDK/app binary mismatch.' }
    }
    # Verification builds are disposable. Keep source starters, ready-to-import packages and all published app files.
    foreach ($sample in @($standard, $native)) {
        foreach ($name in @('bin', 'obj')) {
            $buildPath = [IO.Path]::GetFullPath((Join-Path $sample $name))
            if ([IO.Path]::GetDirectoryName($buildPath) -ne [IO.Path]::GetFullPath($sample)) { throw 'Unexpected sample build path.' }
            if (Test-Path -LiteralPath $buildPath) { Remove-Item -LiteralPath $buildPath -Recurse -Force }
        }
    }
    $assemblies = foreach ($name in $contractNames) {
        @{ name = $name; version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $staging "sdk/net10.0/$name.dll")).Version.ToString() }
    }
    Write-Utf8 'versions.json' (@{
        apiVersion = 1; configuration = $Configuration; dotnetSdkUsed = [string]$sdkUsed; minimumSdk = $minimumSdk
        targetFramework = $framework; windowsTargetFramework = $windowsFramework; windowsAppSdk = $windowsAppSdk
        runtime = 'win-x64'; appIncluded = (-not $SkipApp); appSelfContained = ((-not $SkipApp) -and $SelfContainedApp.IsPresent); assemblies = @($assemblies)
    } | ConvertTo-Json -Depth 5)
    $checksums = Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object FullName | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.FullName.Substring($staging.Length + 1).Replace('\', '/')
    }
    Write-Utf8 'SHA256SUMS.txt' (($checksums -join "`n") + "`n")
    if (-not $NoZip) {
        Write-Host 'Creating shareable ZIP…'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($staging, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
        [IO.File]::Move($temporaryZip, $zipDestination)
    }
    [IO.Directory]::Move($staging, $destination)
    Write-Host "Developer kit: $destination"
    if (-not $NoZip) { Write-Host "Share this archive: $zipDestination" }
    [pscustomobject]@{ Directory = $destination; Zip = $(if ($NoZip) { $null } else { $zipDestination }) }
}
finally {
    Pop-Location
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -eq $parent -and
        [IO.Path]::GetFileName($resolvedStaging) -match '^\.developer-kit-[a-f0-9]{32}$') {
        if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
    }
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
}
