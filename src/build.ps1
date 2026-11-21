$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "Could not find .NET Framework C# compiler at $csc"
}

# Regenerate PNG & ICO from Images\Witchinghour.webp if Python + Pillow is available
$convertScript = Join-Path $scriptDir "convert_assets.py"
if ((Get-Command py -ErrorAction SilentlyContinue) -and (Test-Path $convertScript)) {
    & py $convertScript
    if ($LASTEXITCODE -ne 0) {
        throw "Asset conversion failed."
    }
}

$outExe = Join-Path $rootDir "Witching Hour Bringer.exe"
$srcCs = Join-Path $scriptDir "Program.cs"
$manifest = Join-Path $scriptDir "app.manifest"
$ico = Join-Path $scriptDir "WitchingHour.ico"
$png = Join-Path $scriptDir "WitchingHour.png"

Write-Host "Compiling $outExe ..."
& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    /out:"$outExe" `
    /win32icon:"$ico" `
    /win32manifest:"$manifest" `
    /resource:"$png,WitchingHour.png" `
    /resource:"$ico,WitchingHour.ico" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.ServiceProcess.dll `
    "$srcCs"

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$info = Get-Item $outExe
Write-Host ("Build succeeded: {0} ({1:N1} KB)" -f $info.FullName, ($info.Length / 1KB))
