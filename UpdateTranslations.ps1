$csvPath = "EmbeddedLocalizations.csv"
$table = Import-Csv -Path $csvPath -Delimiter ';'
$headers = $table | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
$languages = $headers | Where-Object { $_ -ne 'Token' }

foreach ($lang in $languages) {
    $json = [ordered]@{}
    foreach ($row in $table) {
        $token = $row.Token
        if ($token) {
            $translation = $row.$lang
            $safeValue = $translation -replace "`r", "\n" -replace "`n", "\n"
            $json[$token] = $safeValue
        }
    }
    $outFile = "Translations\$lang.json"
    $content = $json | ConvertTo-Json -Depth 10 | ForEach-Object { $_ -replace ':  ', ': ' }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($outFile, $content, $utf8NoBom)
}
