[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot 'TestResults'),
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'ci-publish\win-x64'),
    [string]$DiagnosticsDirectory = (Join-Path $PSScriptRoot 'ci-diagnostics')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$results = [System.IO.Path]::GetFullPath($ResultsDirectory)
$publish = [System.IO.Path]::GetFullPath($PublishDirectory)
$diagnostics = [System.IO.Path]::GetFullPath($DiagnosticsDirectory)
$buildLog = Join-Path $diagnostics 'build.log'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][scriptblock]$Command
    )

    Write-Host "`n=== $Label ===" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

foreach ($path in @($results, $publish, $diagnostics)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

Push-Location $repoRoot
try {
    dotnet --info | Out-File (Join-Path $diagnostics 'dotnet-info.txt') -Encoding utf8
    @(
        "Commit=$(git rev-parse HEAD)",
        "Branch=$(git branch --show-current)",
        "Machine=$env:COMPUTERNAME",
        "Configuration=$Configuration"
    ) | Out-File (Join-Path $diagnostics 'run-context.txt') -Encoding utf8

    Invoke-Checked -Label 'Restore APS solution' -Command {
        dotnet restore APS.slnx
    }

    Write-Host "`n=== Build APS solution ===" -ForegroundColor Cyan
    dotnet build APS.slnx --configuration $Configuration --no-restore 2>&1 |
        Tee-Object -FilePath $buildLog
    $buildExitCode = $LASTEXITCODE
    if ($buildExitCode -ne 0) {
        $errors = Get-Content $buildLog | Where-Object {
            $_ -match '\berror\s+(CS|MSB|NU|NETSDK)\d+' -or
            $_ -match ':\s+error\s+' -or
            $_ -match '\bBuild FAILED\b'
        } | Select-Object -Unique
        foreach ($line in $errors) {
            $safe = ([string]$line).Replace("`r", ' ').Replace("`n", ' ')
            Write-Host "##vso[task.logissue type=error]$safe"
        }
        throw "APS solution build failed with exit code $buildExitCode."
    }

    [xml]$solutionXml = Get-Content -LiteralPath 'APS.slnx' -Raw
    $testProjects = @(
        $solutionXml.Solution.Project |
            ForEach-Object { [string]$_.Path } |
            Where-Object { $_ -match '^tests[\\/].+\.csproj$' }
    )

    if ($testProjects.Count -eq 0) {
        throw 'APS.slnx contains no registered test projects.'
    }

    Write-Host "Registered test projects: $($testProjects.Count)"
    foreach ($testProject in $testProjects) {
        $testName = [System.IO.Path]::GetFileNameWithoutExtension($testProject)
        Invoke-Checked -Label "Run $testName" -Command {
            dotnet test $testProject `
                --configuration $Configuration `
                --no-build `
                --no-restore `
                --logger "trx;LogFileName=$testName.trx" `
                --results-directory $results
        }
    }

    Invoke-Checked -Label 'Publish Windows desktop smoke artifact' -Command {
        dotnet publish src/APS.DesktopHost/APS.DesktopHost.csproj `
            --configuration $Configuration `
            --runtime win-x64 `
            --self-contained true `
            --no-restore `
            -p:PublishReadyToRun=true `
            --output $publish
    }

    git status --porcelain=v1 --branch | Out-File (Join-Path $diagnostics 'git-status-after-build.txt') -Encoding utf8
    Write-Host "`nAPS Windows verification passed."
}
finally {
    Pop-Location
}
