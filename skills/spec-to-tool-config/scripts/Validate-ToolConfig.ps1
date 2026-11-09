[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][ValidateSet('messages', 'parsers', 'generators')][string]$Kind,
    [Parameter(Mandatory = $true)][string]$DefinitionPath
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
$definition = (Resolve-Path -LiteralPath $DefinitionPath).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $repository 'NonStopToolkit.slnx'))) {
    throw 'RepositoryRoot must contain NonStopToolkit.slnx.'
}
if ((Get-Item -LiteralPath $definition).Length -gt 1048576) {
    throw 'Definition exceeds the app loader limit of 1 MiB.'
}
$null = Get-Command dotnet -ErrorAction Stop
$sourceFiles = @(
    'src/NonStopToolkit.Parsing/Definitions/MessageProfile.cs',
    'src/NonStopToolkit.Parsing/Definitions/ParserDefinition.cs',
    'src/NonStopToolkit.Generation/Definitions/GeneratorDefinition.cs',
    'src/NonStopToolkit.Core/Encoding/StrictEncoding.cs'
)
$compileItems = foreach ($sourceFile in $sourceFiles) {
    $sourcePath = (Resolve-Path -LiteralPath (Join-Path $repository $sourceFile)).ProviderPath
    '    <Compile Include="' + [System.Security.SecurityElement]::Escape($sourcePath) + '" />'
}
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$framework = [string]$buildProperties.Project.PropertyGroup.TargetFramework
if ($framework -notmatch '^net\d+\.\d+$') { throw 'Cannot determine the current library target framework.' }

# Compile the actual schema sources without bringing in Desktop or NuGet packages.
$tempRoot = [System.IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
$null = New-Item -ItemType Directory -Path $tempRoot -Force
$workDirectory = Join-Path $tempRoot ('nonstop-config-validation-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $workDirectory
$resultCode = 1
try {
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$framework</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
$($compileItems -join [Environment]::NewLine)
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $workDirectory 'Validate.csproj') -Value $project -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $workDirectory 'NuGet.Config') -Encoding UTF8 -Value '<configuration><packageSources><clear /></packageSources></configuration>'
    $program = @'
using NonStopToolkit.Parsing.Definitions;
using NonStopToolkit.Generation.Definitions;

try
{
    var json = File.ReadAllText(args[0]);
    var id = args[1] switch
    {
        "messages" => MessageProfile.FromJson(json).Id,
        "parsers" => ParserDefinition.FromJson(json).Id,
        "generators" => GeneratorDefinition.FromJson(json).Id,
        _ => throw new ArgumentException("Unknown definition kind.")
    };
    Console.WriteLine($"Valid {args[1]} definition: {id}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Invalid {args[1]} definition: {ex.Message}");
    return 1;
}
'@
    Set-Content -LiteralPath (Join-Path $workDirectory 'Program.cs') -Value $program -Encoding UTF8
    Push-Location -LiteralPath $repository
    try {
        & dotnet run --project (Join-Path $workDirectory 'Validate.csproj') --verbosity quiet -- $definition $Kind
        $resultCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}
finally {
    # Only remove the exact, newly created child of the repository artifacts root.
    $resolvedWork = [System.IO.Path]::GetFullPath($workDirectory)
    $parentPath = [System.IO.Path]::GetDirectoryName($resolvedWork).TrimEnd('\', '/')
    if ($parentPath -eq $tempRoot.TrimEnd('\', '/') -and
        [System.IO.Path]::GetFileName($resolvedWork) -match '^nonstop-config-validation-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit $resultCode
