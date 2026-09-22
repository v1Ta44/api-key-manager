# Build and check local release candidates. Never publishes or uses a real vault.
param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$run = Join-Path $root ('artifacts\release-check-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $run | Out-Null
$dotnetExe = (Get-Command $DotnetPath -ErrorAction Stop).Source
$sdkRoot = Split-Path -Parent $dotnetExe
$oldRuntime = $env:DOTNET_ROOT_X64
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
$checks = [Collections.Generic.List[string]]::new()
$result = [ordered]@{ Status = 'running'; CreatedAt = (Get-Date).ToString('o'); Output = $run }
function Check([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw $Label }
    $checks.Add($Label)
}
function Start-App([string]$Exe, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    $owned.Add($process)
    return $process
}
function Wait-Window([Diagnostics.Process]$Process) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $Process.Refresh()
        if ($Process.HasExited) { throw 'Application exited before window became ready.' }
        if ($Process.MainWindowHandle -ne 0) { return $Process.MainWindowTitle }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 20)
    throw 'Window startup timed out.'
}
function Close-App([Diagnostics.Process]$Process, [int]$ExpectedExit) {
    for ($attempt = 0; $attempt -lt 20 -and -not $Process.HasExited; $attempt++) {
        $Process.Refresh()
        $null = $Process.CloseMainWindow()
        $null = $Process.WaitForExit(500)
    }
    Check ($Process.HasExited -and $Process.ExitCode -eq $ExpectedExit) "Owned process exited with expected code $ExpectedExit"
}
function Run-Tool([string]$Exe, [string[]]$Arguments) {
    $process = Start-App $Exe $Arguments
    Check ($process.WaitForExit(120000)) 'Packaged check completed within timeout'
    Check ($process.ExitCode -eq 0) 'Packaged check returned success'
}
Push-Location $root
try {
    $env:DOTNET_ROOT_X64 = $sdkRoot
    $result.Sdk = (& $dotnetExe --version).Trim()
    Check ($LASTEXITCODE -eq 0 -and $result.Sdk -eq '9.0.318') 'Pinned SDK selected'
    & $dotnetExe restore ApiKeyManager.sln -r win-x64 --locked-mode -v minimal 2>&1 | Tee-Object (Join-Path $run 'restore.log')
    Check ($LASTEXITCODE -eq 0) 'Locked restore passed'
    & $dotnetExe test ApiKeyManager.sln -c Release --no-restore -v minimal --logger 'trx;LogFileName=tests.trx' --results-directory $run 2>&1 | Tee-Object (Join-Path $run 'tests.log')
    Check ($LASTEXITCODE -eq 0) 'Release unit tests passed'
    [xml]$testReport = Get-Content (Join-Path $run 'tests.trx') -Raw
    $result.UnitTests = $testReport.TestRun.ResultSummary.Counters.passed
    & $dotnetExe build src/ApiKeyManager.Wpf/ApiKeyManager.Wpf.csproj -c Release --no-restore -warnaserror -v minimal 2>&1 | Tee-Object (Join-Path $run 'build.log')
    Check ($LASTEXITCODE -eq 0) 'Release build passed with warnings treated as errors'
    foreach ($kind in @('vulnerable', 'deprecated')) {
        $auditText = & $dotnetExe list ApiKeyManager.sln package "--$kind" --include-transitive --format json
        Check ($LASTEXITCODE -eq 0) "$kind dependency query completed"
        $auditText | Set-Content (Join-Path $run ($kind + '.json')) -Encoding utf8
        $audit = ($auditText -join "`n") | ConvertFrom-Json
        Check (-not ($audit.problems | Where-Object { $_.severity -eq 'error' })) "$kind feed returned no errors"
        $blocking = @()
        $deferred = @()
        foreach ($projectAudit in $audit.projects) {
            $reported = @($projectAudit.frameworks | ForEach-Object { $_.topLevelPackages; $_.transitivePackages } | Where-Object { $null -ne $_ })
            foreach ($package in $reported) {
                # Narrow, disclosed exception: existing v2 test-only framework. Never shipped.
                $knownTestDependency = $projectAudit.path.Replace('\', '/').EndsWith('/tests/ApiKeyManager.Tests/ApiKeyManager.Tests.csproj') -and
                    $package.id -in @('xunit', 'xunit.assert', 'xunit.core', 'xunit.extensibility.core', 'xunit.extensibility.execution') -and
                    $package.resolvedVersion -eq '2.9.2' -and ($package.deprecationReasons -join ',') -eq 'Legacy'
                if ($kind -eq 'deprecated' -and $knownTestDependency) { $deferred += $package }
                else { $blocking += $package }
            }
        }
        if ($deferred.Count -gt 0) { $result.DeferredTestDeprecations = $deferred }
        Check ($blocking.Count -eq 0) "No blocking $kind dependencies (test-only Legacy exceptions recorded)"
    }
    & (Join-Path $PSScriptRoot 'verify-publish.ps1') -DotnetPath $dotnetExe -OutRoot (Join-Path $run 'publish')
    $publish = @(Get-ChildItem (Join-Path $run 'publish') -Directory)
    Check ($publish.Count -eq 1) 'One unique candidate build directory'
    # Windows PowerShell 5.1 emits a JSON array as one pipeline object; do not wrap it again.
    $built = Get-Content (Join-Path $publish[0].FullName 'publish-results.json') -Raw | ConvertFrom-Json
    $result.Candidates = $built
    $sourceFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
    $source = @($sourceFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | ForEach-Object {
        [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
    $source | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'source-inventory.json') -Encoding utf8
    $result.SourceHead = (& git rev-parse HEAD).Trim()
    $result.SourceInventorySha256 = (Get-FileHash (Join-Path $run 'source-inventory.json') -Algorithm SHA256).Hash
    $packages = @()
    foreach ($candidate in $built) {
        Check ($candidate.Runtime -eq '.NET 9.0.20') "$($candidate.Kind) actually ran on runtime 9.0.20"
        $stage = Join-Path $run ('package-' + $candidate.Kind)
        New-Item -ItemType Directory -Path $stage | Out-Null
        Copy-Item -Path (Join-Path $candidate.Directory '*') -Destination $stage
        Copy-Item -LiteralPath (Join-Path $root 'docs/release-notes-2.0.md') -Destination (Join-Path $stage 'README.md')
        Copy-Item -LiteralPath (Join-Path $sdkRoot 'LICENSE.txt') -Destination (Join-Path $stage 'DOTNET-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $sdkRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $stage 'DOTNET-THIRD-PARTY-NOTICES.txt')
        $assets = Get-Content (Join-Path $root 'src/ApiKeyManager.Wpf/obj/project.assets.json') -Raw | ConvertFrom-Json
        $nugetRoot = @($assets.packageFolders.PSObject.Properties.Name)[0]
        foreach ($name in @('system.drawing.common', 'microsoft.win32.systemevents')) {
            $packageDir = Join-Path $nugetRoot ($name + '/9.0.20')
            Copy-Item -LiteralPath (Join-Path $packageDir 'LICENSE.TXT') -Destination (Join-Path $stage ($name + '-LICENSE.txt'))
            $notices = Join-Path $packageDir 'THIRD-PARTY-NOTICES.TXT'
            if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination (Join-Path $stage ($name + '-NOTICES.txt')) }
        }
        $bad = @(Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -match '\.(akv|akvbak|bak|csv|pfx|key)$|^settings\.json$|candidate-|^test-data' })
        Check ($bad.Count -eq 0) "$($candidate.Kind) package contains no vaults, settings, test data or signing keys"
        $files = @(Get-ChildItem $stage -File | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
        $manifest = [ordered]@{ Version = '2.0.0'; Channel = 'release-candidate'; Platform = 'win-x64'; Kind = $candidate.Kind; Runtime = '9.0.20'; Sdk = $result.Sdk; SourceInventorySha256 = $result.SourceInventorySha256; Files = $files }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $stage 'manifest.json') -Encoding utf8
        $zip = Join-Path $run ('api-key-manager-2.0.0-win-x64-' + $candidate.Kind + '-candidate.zip')
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
        # A space in this directory exercises quoting in the actual packaged executable path.
        $extracted = Join-Path $run ('extracted package ' + $candidate.Kind)
        Expand-Archive -LiteralPath $zip -DestinationPath $extracted
        foreach ($file in $files) { Check ((Get-FileHash (Join-Path $extracted $file.Name) -Algorithm SHA256).Hash -eq $file.Sha256) "$($candidate.Kind) archive integrity: $($file.Name)" }
        $exe = Join-Path $extracted 'ApiKeyManager.exe'
        Run-Tool $exe @('--selftest')
        $dataDir = Join-Path $run ('synthetic data ' + $candidate.Kind)
        $first = Start-App $exe @('--demo', '--dir', ('"' + $dataDir + '"'))
        Check (-not [string]::IsNullOrWhiteSpace((Wait-Window $first))) "$($candidate.Kind) extracted demo window starts"
        $duplicate = Start-App $exe @('--dir', ('"' + $dataDir + '"'))
        $null = Wait-Window $duplicate
        Close-App $duplicate 1
        Close-App $first 0
        $vaultPath = Join-Path $dataDir 'vault.akv'
        $vaultHash = (Get-FileHash $vaultPath -Algorithm SHA256).Hash
        $demoAgain = Start-App $exe @('--demo', '--dir', ('"' + $dataDir + '"'))
        $null = Wait-Window $demoAgain
        Close-App $demoAgain 1
        Check ((Get-FileHash $vaultPath -Algorithm SHA256).Hash -eq $vaultHash) "$($candidate.Kind) demo refuses to overwrite existing vault"
        $reopened = Start-App $exe @('--dir', ('"' + $dataDir + '"'))
        Check (-not [string]::IsNullOrWhiteSpace((Wait-Window $reopened))) "$($candidate.Kind) normal locked restart starts"
        Close-App $reopened 0
        Check ((Get-FileHash $vaultPath -Algorithm SHA256).Hash -eq $vaultHash) "$($candidate.Kind) locked restart preserves vault bytes"
        Check (-not (Test-Path (Join-Path $extracted 'vault.akv'))) "$($candidate.Kind) explicit data directory isolates package files"
        $packages += [pscustomobject]@{ Kind = $candidate.Kind; Archive = $zip; Sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash; Bytes = (Get-Item $zip).Length; Signature = (Get-AuthenticodeSignature $exe).Status.ToString(); Manifest = $manifest }
    }
    $result.Packages = $packages
    $packages | ForEach-Object { $_.Sha256 + '  ' + [IO.Path]::GetFileName($_.Archive) } | Set-Content (Join-Path $run 'SHA256SUMS.txt') -Encoding ascii
    $result.Status = 'automated-checks-passed-manual-gates-pending'
} catch {
    $result.Status = 'failed'
    $result.Failure = $_.Exception.Message
    throw
} finally {
    foreach ($process in $owned) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
    $env:DOTNET_ROOT_X64 = $oldRuntime
    $result.Checks = $checks.ToArray()
    $result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $run 'release-checks.json') -Encoding utf8
    Pop-Location
    Write-Output "Release evidence: $run"
}
