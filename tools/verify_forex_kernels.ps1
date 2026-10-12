[CmdletBinding()]
param(
    [string]$Dll='src/Besm6.Cli/bin/Release/net8.0/besm6.dll',
    [ValidateSet('memspeed','mpmflops')][string[]]$Benchmarks=@('memspeed','mpmflops'),
    [string]$OutputDirectory=('tests-run/forex-kernels-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateSet('original','virtual','max')][string[]]$Modes=@('original','virtual','max'),
    [switch]$CheckGuards
)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent $PSScriptRoot
function Reference-Mp([int]$Part,[int]$Passes=30){
    [decimal]$value=0.999999
    $a=@([decimal]0.000020,[decimal]0.000011,[decimal]0.000012,[decimal]0.000013,[decimal]0.000014,[decimal]0.000015,[decimal]0.000016,[decimal]0.000017,[decimal]0.000018,[decimal]0.000019,[decimal]0.000021)
    $b=@([decimal]0.999980,[decimal]1.000011,[decimal]0.999992,[decimal]1.000013,[decimal]0.999994,[decimal]1.000015,[decimal]0.999996,[decimal]1.000017,[decimal]1.000018,[decimal]1.000019,[decimal]1.000021)
    for($pass=0;$pass -lt $Passes;$pass++){
        if($Part -eq 1){$value=($value+[decimal]0.000020)*[decimal]0.999950}
        elseif($Part -gt 1){[decimal]$sum=0; $terms=if($Part -eq 2){3}else{11}; for($i=0;$i -lt $terms;$i++){[decimal]$term=($value+$a[$i])*$b[$i]; if($i%2 -eq 0){$sum+=$term}else{$sum-=$term}}; $value=$sum}
    }
    return [double]$value
}
Push-Location -LiteralPath $repositoryRoot
try {
    if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a fresh output directory.'}
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $config=Join-Path $OutputDirectory 'config.json'
    @{tapes=(Join-Path $repositoryRoot 'tapes');useWallClock=$false;forexAsFortran=$false} | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding utf8
    $cases=@()
    foreach($family in $Benchmarks){
        foreach($part in 1..3){$cases+=@{family=$family;part=$part;scenario='good'}}
        if($CheckGuards){foreach($scenario in @('zero','off','bad','short','invalid')){$cases+=@{family=$family;part=$(if($scenario -eq 'zero'){0}elseif($scenario -eq 'invalid'){4}else{1});scenario=$scenario}}}
    }
    $results=@();$culture=[Globalization.CultureInfo]::InvariantCulture
    foreach($case in $cases){
        $source=Get-Content ('examples/'+$case.family+'-forex.dub') -Raw
        $source=$source.Replace('data kpart/1/',('data kpart/'+$case.part+'/'))
        if($case.scenario -eq 'bad'){$source=$source.Replace('      nbad=0',"      x(1)=0.`n      nbad=0").Replace('      nbad = 0',"      x(1)=0.`n      nbad = 0")}
        if($case.scenario -eq 'short'){
            if($case.family -eq 'memspeed'){$source=$source.Replace('nwrds/4096/, rpts/1000/','nwrds/1/, rpts/1/').Replace('nwrds .ne. 4096 .or. rpts .ne. 1000','nwrds .ne. 1 .or. rpts .ne. 1')}
            else{$ref=(Reference-Mp 1 1).ToString('F14',$culture);$source=$source.Replace('nwrds/9000/, rpts/30/','nwrds/1/, rpts/1/').Replace('nwrds .ne. 9000 .or. rpts .ne. 30','nwrds .ne. 1 .or. rpts .ne. 1').Replace('refval=0.99909962371626',('refval='+$ref))}
        }
        $name=$case.family+'-'+$case.part+'-'+$case.scenario
        $deck=Join-Path $OutputDirectory ($name+'.dub');Set-Content -LiteralPath $deck -Value $source -Encoding utf8
        $selectedModes=if($case.scenario -eq 'good'){$Modes}else{@('max')}
        $modelState=$null
        foreach($mode in $selectedModes){
            Write-Host "Running $name $mode"
            $clockArgs=@();if($case.scenario -ne 'off'){$clockArgs=@('--guest-clock')}
            $lines=& dotnet $Dll run $deck --config $config --speed $mode --stats --no-wall-clock --no-hang-detect --limit 200000000 @clockArgs 2>&1
            $code=$LASTEXITCODE;$output=($lines | ForEach-Object {$_.ToString()}) -join "`n"
            Set-Content -LiteralPath (Join-Path $OutputDirectory ($name+'-'+$mode+'.out')) -Value $output -Encoding utf8
            if($code -ne 0 -or $output -notmatch 'Halted by STOP' -or $output -notmatch 'F O R E X' -or $output -match 'did not terminate'){throw 'Native guest run failed'}
            $program=($output -split '≠')[-1].Split('Halted by STOP')[0].Trim()
            $unit=if($case.family -eq 'memspeed'){'MB/S'}else{'MFLOPS'}
            $ratingMatch=[regex]::Match($program,'(?m)^GUEST '+[regex]::Escape($unit)+':\s*(\S+)')
            function Number([string]$Label){$m=[regex]::Match($program,'(?m)^'+[regex]::Escape($Label)+'\s*(\S+)');if(-not $m.Success){throw "Missing $Label"};return [double]::Parse($m.Groups[1].Value,$culture)}
            $rating=$null;$seconds=$null
            if($case.scenario -eq 'good'){
                if($case.family -eq 'memspeed'){
                    if($program -notmatch 'BAD ELEMENTS=\s*0\b' -or -not $program.Contains('ALL VALUES MATCH: YES')){throw 'Memory controls failed'}
                    $byelem=if($case.part -eq 1){12}else{18};$work=4096.0*1000*$byelem
                    $first=if($case.part -eq 1){1.0/4096}elseif($case.part -eq 2){1+1000.0/4096}else{1+500.0/4096}
                    $last=if($case.part -eq 1){1}elseif($case.part -eq 2){1001}else{501}
                    $sum=if($case.part -eq 1){2048.5}elseif($case.part -eq 2){2052596}else{1028346}
                    $expected=@{'WORDS='=4096;'REPEAT PASSES='=1000;'BYTES/ELEMENT='=$byelem;'DATA TRAFFIC BYTES='=$work;'FIRST RESULT='=$first;'LAST RESULT='=$last;'CHECKSUM='=$sum}
                }else{
                    if($program -notmatch 'BAD CONTROL VALUES=\s*0\b' -or -not $program.Contains('ALL SAME: YES') -or -not $program.Contains('CALCULATIONS PERFORMED')){throw 'Floating point controls failed'}
                    $ops=@(0,2,8,32)[$case.part];$work=9000.0*30*$ops
                    $expected=@{'WORDS='=9000;'REPEAT PASSES='=30;'OPS/WORD='=$ops;'FLOPS='=$work;'FIRST RESULT='=(Reference-Mp $case.part)}
                }
                foreach($label in $expected.Keys){$actual=Number $label;$ref=[double]$expected[$label];if([Math]::Abs($actual-$ref) -gt [Math]::Max(1e-9,[Math]::Abs($ref)*1e-9)){throw "Independent check failed: $label"}}
                $seconds=Number 'GUEST CPU SECONDS:'
                if(-not $ratingMatch.Success){throw 'Rating missing'}
                $rating=[double]::Parse($ratingMatch.Groups[1].Value,$culture);$ref=$work/(1000000*$seconds)
                if($seconds -lt .04 -or $rating -le 0 -or [Math]::Abs($rating-$ref) -gt [Math]::Max(.0000006,[Math]::Abs($ref)*.000002)){throw 'Rating formula failed'}
            }else{
                $reason=if($case.scenario -eq 'zero'){'ZERO KERNEL'}elseif($case.scenario -in @('bad','invalid')){'CONTROL VALUES DO NOT MATCH'}else{'ENABLE --GUEST-CLOCK OR RUN LONGER'}
                if($ratingMatch.Success -or -not $program.Contains($unit+' UNAVAILABLE: '+$reason)){throw "Guard failed: $name"}
                if($case.scenario -in @('off','short')){ $control=if($case.family -eq 'memspeed'){'ALL VALUES MATCH: YES'}else{'ALL SAME: YES'};if(-not $program.Contains($control)){throw 'Timer guard failed numerical controls'} }
            }
            $stats=[regex]::Match($output,'(?m)^Execution stats: (.+)$').Groups[1].Value | ConvertFrom-Json
            $state=@{program=$program;finalState=$stats.finalState;memorySha256=$stats.memorySha256;instructions=$stats.statistics.completedInstructions;cycles=$stats.statistics.modelCycles} | ConvertTo-Json -Depth 20 -Compress
            if($mode -ne 'virtual'){if($null -ne $modelState -and $modelState -cne $state){throw 'Original/max output or state differs'};$modelState=$state}
            $results+=[pscustomobject]@{benchmark=$case.family;part=$case.part;scenario=$case.scenario;mode=$mode;validated=$true;rating=$rating;unit=$unit;guestSeconds=$seconds;stats=$stats}
            $results | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Encoding utf8
            Write-Host "$name $mode passed: $rating $unit"
        }
    }
    $results | Select-Object benchmark,part,scenario,mode,validated,rating,unit,guestSeconds | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'results.csv') -Encoding utf8 -NoTypeInformation
}finally{Pop-Location}
