param([switch]$FrameworkDependent, [string]$OutputDirectory = 'artifacts\windows-x64')
$ErrorActionPreference = 'Stop'
$antSdkCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$antSdk = if ($antSdkCommand) { $antSdkCommand.Source } else { $null }
if (-not $antSdk -or -not (& $antSdk --list-sdks)) {
    $antPortableSdk = Join-Path $env:LOCALAPPDATA 'AntFieldBuild\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $antPortableSdk) { $antSdk = $antPortableSdk }
    else { throw '请安装 .NET 8 SDK，再运行此构建脚本。' }
}
$antProject = Join-Path $PSScriptRoot 'AntField.Desktop\AntField.Desktop.csproj'
$antTests = Join-Path $PSScriptRoot 'AntField.Core.Tests\AntField.Core.Tests.csproj'
$antOutput = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
& $antSdk run --project $antTests -c Release
if ($LASTEXITCODE -ne 0) { throw '蚁群行为测试失败。' }
$antSelfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $antSdk publish $antProject -c Release -r win-x64 --self-contained $antSelfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $antOutput
if ($LASTEXITCODE -ne 0) { throw '桌面版打包失败。' }
Write-Host "桌面版已生成：$(Join-Path $antOutput 'AntField.exe')"
