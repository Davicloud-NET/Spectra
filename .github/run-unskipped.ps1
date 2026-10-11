<#
.SYNOPSIS
    Runs a test suite and fails when a test skipped itself or none ran.

.DESCRIPTION
    A test that needs build output, such as a native library, skips when the
    output is missing. In CI the output is meant to be there, so a skip means
    the suite ran without what it tests. A filter that selects nothing means
    the same, and the runner exits with 0 for it.

.PARAMETER Project
    The test project, as dotnet run takes it.

.PARAMETER Filter
    Arguments for the test runner, each one quoted: -Filter '-trait', 'Suite=CrashCases'
#>
param(
    [Parameter(Mandatory)]
    [string]$Project,

    [string[]]$Filter = @()
)

$output = dotnet run --project $Project -- @Filter 2>&1 | Out-String
$code = $LASTEXITCODE
Write-Host $output
if ($code -ne 0) { exit $code }

if ($output -notmatch 'Total:\s*(\d+)') {
    Write-Host "::error title=Test summary unreadable::$Project printed no test count, so this step cannot say whether its tests ran."
    exit 1
}
if ([int]$Matches[1] -eq 0) {
    Write-Host "::error title=No tests ran::The filter '$Filter' selected no test of $Project. A trait or a class was probably renamed."
    exit 1
}

# With no skip the runner still prints the count, as 0.
if ($output -notmatch 'Skipped:\s*(\d+)') {
    Write-Host "::error title=Test summary unreadable::$Project printed no skip count, so this step cannot say whether its tests ran."
    exit 1
}
if ([int]$Matches[1] -gt 0) {
    Write-Host "::error title=Tests skipped::$($Matches[1]) test(s) of $Project skipped themselves. The reasons are in the log above."
    exit 1
}
