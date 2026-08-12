[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [int]$Rows = 1000000
)

$encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.IO.StreamWriter]::new($Path, $false, $encoding, 1048576)
try {
    $writer.WriteLine('id,name,city,note')
    for ($index = 0; $index -lt $Rows; $index++) {
        $writer.Write($index)
        $writer.Write(',Person ')
        $writer.Write($index)
        $writer.Write(',Managua,"Generated record ')
        $writer.Write($index)
        $writer.WriteLine('"')
    }
}
finally {
    $writer.Dispose()
}

Get-Item -LiteralPath $Path | Select-Object FullName, Length

