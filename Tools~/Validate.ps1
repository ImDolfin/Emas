<#
.SYNOPSIS
Runs the included Unity project's EditMode and PlayMode tests.
.EXAMPLE
.\Tools~\Validate.ps1 -UnityPath 'D:\Unity\2022.3.62f3\Editor\Unity.exe'
#>
[CmdletBinding()]
param(
    [string]$UnityPath = $env:UNITY_PATH,
    [ValidateSet('All', 'EditMode', 'PlayMode')]
    [string]$TestPlatform = 'All'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try
{
    $repository = Split-Path -Parent $PSScriptRoot
    $project = Join-Path $repository 'Tests\Unity~'
    $output = Join-Path $repository 'TestResults\Validation'
    if ([string]::IsNullOrWhiteSpace($UnityPath))
    {
        $UnityPath = Join-Path ${env:ProgramFiles} 'Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
    }

    if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf))
    {
        throw "Unity was not found at '$UnityPath'. Install Unity 2022.3.62f3 or set -UnityPath / UNITY_PATH."
    }

    $UnityPath = (Resolve-Path -LiteralPath $UnityPath).ProviderPath
    if (!(Test-Path -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -PathType Leaf))
    {
        throw "The included Unity project is missing at '$project'."
    }

    New-Item -ItemType Directory -Path $output -Force | Out-Null
}
catch
{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}

$platforms = if ($TestPlatform -eq 'All') { @('EditMode', 'PlayMode') } else { @($TestPlatform) }
$failures = New-Object 'System.Collections.Generic.List[string]'
foreach ($platform in $platforms)
{
    $report = Join-Path $output ($platform + '.xml')
    $log = Join-Path $output ($platform + '.log')
    try
    {
        foreach ($previous in @($report, $log))
        {
            if (Test-Path -LiteralPath $previous)
            {
                Remove-Item -LiteralPath $previous -Force
            }
        }

        Write-Host "Running $platform tests. Log: $log"
        $arguments = @(
            '-batchmode', '-nographics',
            '-projectPath', ('"' + $project + '"'),
            '-runTests', '-testPlatform', $platform,
            '-testResults', ('"' + $report + '"'),
            '-logFile', ('"' + $log + '"')
        )
        $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0)
        {
            throw "Unity exited with code $($process.ExitCode)."
        }

        if (!(Test-Path -LiteralPath $report -PathType Leaf))
        {
            throw 'Unity did not produce a test results XML file.'
        }

        [xml]$results = Get-Content -LiteralPath $report -Raw
        $run = $results.SelectSingleNode('/test-run')
        if ($null -eq $run -or $run.GetAttribute('result') -cne 'Passed')
        {
            throw 'The test run did not report Passed.'
        }

        $total = 0
        $failed = 0
        if (![int]::TryParse($run.GetAttribute('total'), [ref]$total) -or $total -le 0 -or
            ![int]::TryParse($run.GetAttribute('failed'), [ref]$failed) -or $failed -ne 0)
        {
            throw 'The test run has failed tests, no tests, or invalid result counts.'
        }

        Write-Host "$platform passed ($total tests). Results: $report"
    }
    catch
    {
        $message = "${platform}: $($_.Exception.Message) See '$log' and '$report'."
        $failures.Add($message)
        [Console]::Error.WriteLine($message)
    }
}

if ($failures.Count -gt 0)
{
    exit 1
}

exit 0
