# Nova Browser (Android) build script - no Gradle, uses Android SDK command line tools.
# Run from anywhere: powershell -ExecutionPolicy Bypass -File NovaMobile\build-apk.ps1
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$jdk = Join-Path $env:LOCALAPPDATA "NovaBrowserBuild\android\jdk\jdk-17.0.20.1+1"
$sdk = Join-Path $env:LOCALAPPDATA "NovaBrowserBuild\android\sdk"
$bt = Join-Path $sdk "build-tools\35.0.0"
$androidJar = Join-Path $sdk "platforms\android-35\android.jar"

$env:JAVA_HOME = $jdk
$env:PATH = "$(Join-Path $jdk 'bin');$env:PATH"

Push-Location $root
try {
    foreach ($dir in @("build", "build\gen", "build\classes", "build\dex")) {
        New-Item -ItemType Directory -Force $dir | Out-Null
    }
    Get-ChildItem -Recurse -File "build\classes" -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -Recurse -File "build\dex" -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host "[1/7] compile resources"
    & (Join-Path $bt "aapt2.exe") compile --dir "res" -o "build\res.zip"
    if ($LASTEXITCODE -ne 0) { throw "aapt2 compile failed" }

    Write-Host "[2/7] link resources + manifest"
    & (Join-Path $bt "aapt2.exe") link `
        -o "build\app-unsigned.apk" `
        -I $androidJar `
        --manifest "AndroidManifest.xml" `
        -R "build\res.zip" `
        --java "build\gen" `
        -A "assets" `
        --min-sdk-version 23 `
        --target-sdk-version 34 `
        --version-code 1 `
        --version-name 1.0.0 `
        --auto-add-overlay
    if ($LASTEXITCODE -ne 0) { throw "aapt2 link failed" }

    Write-Host "[3/7] compile java"
    $sources = @()
    $sources += Get-ChildItem -Recurse -File "src" -Filter *.java | ForEach-Object { $_.FullName.Substring($root.Length + 1) }
    $sources += Get-ChildItem -Recurse -File "build\gen" -Filter *.java | ForEach-Object { $_.FullName.Substring($root.Length + 1) }
    & (Join-Path $jdk "bin\javac.exe") --release 8 -encoding UTF-8 -classpath $androidJar -d "build\classes" $sources
    if ($LASTEXITCODE -ne 0) { throw "javac failed" }

    Write-Host "[4/7] dex"
    if (Test-Path "build\classes.jar") { Remove-Item -Force "build\classes.jar" }
    & (Join-Path $jdk "bin\jar.exe") cf "build\classes.jar" -C "build\classes" .
    & (Join-Path $bt "d8.bat") --release --min-api 23 --lib $androidJar --output "build\dex" "build\classes.jar"
    if ($LASTEXITCODE -ne 0) { throw "d8 failed" }

    Write-Host "[5/7] package"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $apk = Join-Path $root "build\app-unsigned.apk"
    $zip = [System.IO.Compression.ZipFile]::Open($apk, "Update")
    $existing = $zip.GetEntry("classes.dex")
    if ($null -ne $existing) { $existing.Delete() }
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, (Join-Path $root "build\dex\classes.dex"), "classes.dex",
        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    $zip.Dispose()

    Write-Host "[6/7] align + sign"
    if (-not (Test-Path "build\nova.keystore")) {
        & (Join-Path $jdk "bin\keytool.exe") -genkeypair -keystore "build\nova.keystore" -alias nova `
            -keyalg RSA -keysize 2048 -validity 10000 `
            -storepass novabrowser -keypass novabrowser `
            -dname "CN=Nova Browser, O=Nova Browser, C=RU"
    }
    & (Join-Path $bt "zipalign.exe") -f -p 4 "build\app-unsigned.apk" "build\app-aligned.apk"
    if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }

    & (Join-Path $bt "apksigner.bat") sign --ks "build\nova.keystore" --ks-pass pass:novabrowser `
        --key-pass pass:novabrowser --out "build\NovaBrowser-1.0.0.apk" "build\app-aligned.apk"
    if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }

    Write-Host "[7/7] verify"
    & (Join-Path $bt "apksigner.bat") verify "build\NovaBrowser-1.0.0.apk"
    if ($LASTEXITCODE -ne 0) { throw "signature verification failed" }

    Write-Host ""
    Write-Host "OK: $root\build\NovaBrowser-1.0.0.apk"
}
finally {
    Pop-Location
}
