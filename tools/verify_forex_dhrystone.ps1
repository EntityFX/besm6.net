[CmdletBinding()]
param(
    [string]$Dll='src/Besm6.Cli/bin/Release/net8.0/besm6.dll',
    [string]$OutputDirectory=('tests-run/dhrystone-forex-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateSet('original','virtual','max')][string[]]$Modes=@('original','virtual','max'),
    [switch]$CheckGuards
)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a fresh output directory.'}
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $source=Get-Content examples/dhrystone-forex.dub -Raw
    $config=Join-Path $OutputDirectory 'config.json'
    @{tapes=(Join-Path $repositoryRoot 'tapes');useWallClock=$false;forexAsFortran=$false} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
    $cases=@(@{name='dh5000';passes=5000;scenario='good'},@{name='dh10000';passes=10000;scenario='good'})
    if($CheckGuards){$cases+=@(@{name='zero';passes=0;scenario='zero'},@{name='clock-off';passes=5000;scenario='off'},@{name='bad-control';passes=5000;scenario='bad'},@{name='bad-string';passes=5000;scenario='bad-string'},@{name='short';passes=1;scenario='short'})}
    $results=@()
    $culture=[Globalization.CultureInfo]::InvariantCulture
    foreach($case in $cases){
        $text=$source.Replace('DATA NUMBER/5000/',('DATA NUMBER/'+$case.passes+'/'))
        if($case.scenario -eq 'bad'){$text=$text.Replace('      NBAD=0',"      IGLOB=-1`n      NBAD=0")}
        if($case.scenario -eq 'bad-string'){$text=$text.Replace('      NBAD=0',"      RSTRC(2)='WRONG STRING'`n      NBAD=0")}
        $deck=Join-Path $OutputDirectory ($case.name+'.dub')
        Set-Content -LiteralPath $deck -Value $text -Encoding utf8
        $modelState=$null
        foreach($mode in $Modes){
            Write-Host "Running $($case.name) $mode"
            $clockArgs=@()
            if($case.scenario -ne 'off'){$clockArgs=@('--guest-clock')}
            $lines=& dotnet $Dll run $deck --config $config --speed $mode --stats --no-wall-clock --no-hang-detect --limit 200000000 @clockArgs 2>&1
            $code=$LASTEXITCODE
            $output=($lines | ForEach-Object {$_.ToString()}) -join "`n"
            Set-Content -LiteralPath (Join-Path $OutputDirectory ($case.name+'-'+$mode+'.out')) -Value $output -Encoding utf8
            if($code -ne 0 -or $output -notmatch 'Halted by STOP' -or $output -match 'did not terminate'){throw 'Guest did not complete'}
            if($output -notmatch 'F O R E X'){throw 'Native compiler banner missing'}
            $program=($output -split '≠')[-1].Split('Halted by STOP')[0].Trim().Replace('′',"'")
            $score=[regex]::Match($program,'(?m)^GUEST DMIPS:\s*(\S+)')
            $seconds=$null;$rating=$null
            if($case.scenario -eq 'good'){
                $expected=@{'IGLOB'=5;'A1GLOB 8'=7;'A2GLOB 8/7'=($case.passes+10);'PGLB.DISCR'=0;'PGLB.ENUM'=2;'PGLB.INT'=17;'PGLB.PTR'=2;'NEXTP.DISCR'=0;'NEXTP.ENUM'=1;'NEXTP.INT'=18;'NEXTP.PTR'=2;'I1LOC'=5;'I2LOC'=13;'I3LOC'=7;'ELOC'=1}
                foreach($label in $expected.Keys){
                    $match=[regex]::Match($program,'(?m)^'+[regex]::Escape($label)+':\s*(-?\d+)\s*$')
                    if(-not $match.Success -or [int]$match.Groups[1].Value -ne $expected[$label]){throw "Independent control failed: $label"}
                }
                foreach($item in @(@('BLOB','T'),@('C1GLB','A'),@('C2GLB','B'))){if($program -notmatch ('(?m)^'+$item[0]+':\s*'+$item[1]+'\s*$')){throw 'Boolean/character control failed'}}
                $strings=@{'PGLB.STR'='DHRYSTONE PROGRAM, SOME STRING';'NEXTP.STR'='DHRYSTONE PROGRAM, SOME STRING';'LOCAL STRING1'="DHRYSTONE PROGRAM, 1'ST STRING";'LOCAL STRING2'="DHRYSTONE PROGRAM, 2'ND STRING"}
                foreach($label in $strings.Keys){if($program -notmatch ('(?m)^'+[regex]::Escape($label)+':\s*'+[regex]::Escape($strings[$label])+'\s*$')){throw "Independent string control failed: $label"}}
                if($program -notmatch 'BAD CONTROL VALUES=\s*0\b' -or -not $program.Contains('ALL 18 CONTROL VALUES MATCH THE DHRYSTONE 2.1 SPEC') -or -not $program.Contains('ALL 4 STRING VALUES MATCH: YES')){throw 'Guest controls failed'}
                $timeMatch=[regex]::Match($program,'(?m)^GUEST CPU SECONDS:\s*(\S+)')
                if(-not $score.Success -or -not $timeMatch.Success){throw 'Guest rating missing'}
                $rating=[double]::Parse($score.Groups[1].Value,$culture)
                $seconds=[double]::Parse($timeMatch.Groups[1].Value,$culture)
                $reference=$case.passes/($seconds*1757)
                if($seconds -lt .04 -or $rating -le 0 -or [Math]::Abs($rating-$reference) -gt [Math]::Max(.0000006,[Math]::Abs($reference)*.000002)){throw 'Guest rating formula failed'}
            }else{
                $reason=if($case.scenario -eq 'zero'){'ZERO PASSES'}elseif($case.scenario -in @('bad','bad-string')){'CONTROL VALUES DO NOT MATCH'}else{'ENABLE --GUEST-CLOCK OR RUN LONGER'}
                if($score.Success -or -not $program.Contains('DMIPS UNAVAILABLE: '+$reason)){throw "Guard failed: $($case.name)"}
                if($case.scenario -in @('off','short') -and $program -notmatch 'BAD CONTROL VALUES=\s*0\b'){throw 'Timer guard did not reach valid controls'}
            }
            $stats=[regex]::Match($output,'(?m)^Execution stats: (.+)$').Groups[1].Value | ConvertFrom-Json
            $state=@{program=$program;finalState=$stats.finalState;memorySha256=$stats.memorySha256;instructions=$stats.statistics.completedInstructions;cycles=$stats.statistics.modelCycles} | ConvertTo-Json -Depth 20 -Compress
            if($mode -ne 'virtual'){
                if($null -ne $modelState -and $modelState -cne $state){throw 'Original/max guest output or final state differs'}
                $modelState=$state
            }
            $results+=[pscustomobject]@{case=$case.name;passes=$case.passes;mode=$mode;validated=$true;dmips=$rating;guestSeconds=$seconds;stats=$stats}
            $results | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Encoding utf8
            Write-Host "$($case.name) $mode passed: $rating DMIPS"
        }
    }
    $results | Select-Object case,passes,mode,validated,dmips,guestSeconds | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'results.csv') -Encoding utf8 -NoTypeInformation
}finally{Pop-Location}
