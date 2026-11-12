[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$package = 'com.meta.hackathon.thinkingspace'
$remoteDirectory = "/sdcard/Android/data/$package/files/ThinkingSpace"
$remoteFile = "$remoteDirectory/dev-token.txt"
$temporaryFile = $null

function Invoke-AdbChecked {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & adb @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ADB command failed with exit code $LASTEXITCODE."
    }
}

try {
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
        throw 'adb was not found on PATH. Install Android platform tools and open a new terminal.'
    }

    $deviceLines = @(& adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' })
    $authorized = @($deviceLines | Where-Object { $_ -match "\sdevice$" })
    if ($authorized.Count -ne 1 -or $deviceLines.Count -ne 1) {
        throw "Connect exactly one authorized Quest. 'adb devices' currently reports $($deviceLines.Count) device entries."
    }

    & adb shell pm path $package *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Thinking Space ($package) is not installed on the connected Quest. Run the app once before provisioning."
    }

    $secureToken = Read-Host 'Enter the privately supplied Thinking Space development token' -AsSecureString
    $credential = [pscredential]::new('token', $secureToken)
    $plainToken = $credential.GetNetworkCredential().Password
    if ([string]::IsNullOrWhiteSpace($plainToken)) {
        throw 'No token was supplied.'
    }

    $temporaryFile = Join-Path ([IO.Path]::GetTempPath()) ("thinking-space-token-{0}.tmp" -f [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($temporaryFile, $plainToken.Trim(), [Text.UTF8Encoding]::new($false))
    $plainToken = $null
    $credential = $null
    $secureToken.Dispose()

    Invoke-AdbChecked @('shell', 'mkdir', '-p', $remoteDirectory)
    Invoke-AdbChecked @('push', $temporaryFile, $remoteFile)
    & adb shell test -s $remoteFile
    if ($LASTEXITCODE -ne 0) {
        throw 'The token file was copied but could not be verified as nonempty.'
    }

    Write-Host 'Development authentication was provisioned successfully. Restart Thinking Space.'
    Write-Host "Quest location: $remoteFile"
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    if ($temporaryFile -and (Test-Path -LiteralPath $temporaryFile)) {
        Remove-Item -LiteralPath $temporaryFile -Force
    }
}
