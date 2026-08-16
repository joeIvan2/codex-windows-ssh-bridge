[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot '..\src\CodexSshBridge.cs'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Bridge source was not found."
}

$cscCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$cscPath = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $cscPath) {
    throw "The .NET Framework C# compiler (csc.exe) was not found."
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

function Invoke-CscBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EntryPoint,

        [Parameter(Mandatory = $true)]
        [string]$OutputName
    )

    $outputPath = Join-Path $resolvedOutput $OutputName
    $arguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        ("/out:{0}" -f $outputPath),
        ("/main:{0}" -f $EntryPoint),
        $sourcePath
    )

    & $cscPath @arguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "Compilation failed for $OutputName."
    }
}

Invoke-CscBuild -EntryPoint 'CodexSshBridge' -OutputName 'codex-ssh-bridge.exe'
Invoke-CscBuild -EntryPoint 'CodexSshLoginShell' -OutputName 'codex-ssh-login-shell.exe'

[pscustomobject]@{
    OutputDirectory = $resolvedOutput
    Bridge = Join-Path $resolvedOutput 'codex-ssh-bridge.exe'
    LoginShellShim = Join-Path $resolvedOutput 'codex-ssh-login-shell.exe'
}
