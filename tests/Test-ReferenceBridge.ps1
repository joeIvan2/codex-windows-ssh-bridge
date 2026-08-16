[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot 'src\CodexSshBridge.cs'
$buildScript = Join-Path $repositoryRoot 'build\Build-ReferenceBridge.ps1'
$temporaryOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("codex-ssh-reference-test-" + [guid]::NewGuid().ToString('N'))

try {
    $source = Get-Content -LiteralPath $sourcePath -Raw
    foreach ($forbiddenFragment in @(
        'File.AppendAllText',
        'ssh-logs',
        'bash -s',
        'cmd.exe',
        'PowerShell',
        'tee'
    )) {
        if ($source.IndexOf($forbiddenFragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Reference source contains forbidden runtime fragment: $forbiddenFragment"
        }
    }

    & $buildScript -OutputDirectory $temporaryOutput | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Reference bridge build failed.'
    }

    foreach ($binaryName in @('codex-ssh-bridge.exe', 'codex-ssh-login-shell.exe')) {
        $binaryPath = Join-Path $temporaryOutput $binaryName
        $output = & $binaryPath --self-test 2>&1
        if ($LASTEXITCODE -ne 0 -or $output -notmatch 'self-test: OK') {
            throw "$binaryName self-test failed."
        }
    }

    Write-Output 'Reference bridge build and self-tests passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryOutput) {
        Remove-Item -LiteralPath $temporaryOutput -Recurse -Force
    }
}
