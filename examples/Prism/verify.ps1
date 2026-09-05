param([Parameter(Mandatory = $true)][string]$Path)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Number([string]$value) {
    $number = [double]::Parse($value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture)
    Require ([double]::IsFinite($number)) 'Coordinates must be finite.'
    return $number
}
function Points($node, [int]$count) {
    $tokens = @($node.GetAttribute('points').Trim() -split '[,\s]+')
    Require ($tokens.Count -eq 2 * $count) "Expected $count coordinate pairs."
    $points = for ($i = 0; $i -lt $tokens.Count; $i += 2) {
        $point = @((Number $tokens[$i]), (Number $tokens[$i + 1]))
        Require ($point[0] -ge 0 -and $point[0] -le 1200 -and $point[1] -ge 0 -and $point[1] -le 580) 'Geometry is outside the viewport.'
        ,$point
    }
    return ,$points
}
function Distance($a, $b) { return [Math]::Sqrt([Math]::Pow($b[0] - $a[0], 2) + [Math]::Pow($b[1] - $a[1], 2)) }
function Angle($a, $b) { return [Math]::Atan2($a[1] - $b[1], $b[0] - $a[0]) * 180 / [Math]::PI }
function OnFace($point, $a, $b) {
    $length = Distance $a $b
    $distance = [Math]::Abs(($b[0] - $a[0]) * ($point[1] - $a[1]) - ($b[1] - $a[1]) * ($point[0] - $a[0])) / $length
    Require ($distance -le 0.002 -and (Distance $point $a) -le $length -and (Distance $point $b) -le $length) 'Ray missed its prism face.'
}
$settings = [Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
$settings.XmlResolver = $null
$settings.MaxCharactersInDocument = 1MB
$stream = [IO.File]::OpenRead((Get-Item -LiteralPath $Path).FullName)
try {
    $reader = [Xml.XmlReader]::Create($stream, $settings)
    try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader) }
    finally { $reader.Dispose() }
} finally { $stream.Dispose() }
$ns = [Xml.XmlNamespaceManager]::new($document.NameTable)
$ns.AddNamespace('s', 'http://www.w3.org/2000/svg')
$root = $document.DocumentElement
Require ($root.LocalName -ceq 'svg' -and $root.GetAttribute('viewBox') -ceq '0 0 1200 580') 'Unexpected SVG viewport.'
Require ((Number $root.GetAttribute('width')) -eq 1200 -and (Number $root.GetAttribute('height')) -eq 580 -and $root.GetAttribute('preserveAspectRatio') -cin @('', 'xMidYMid meet')) 'SVG dimensions must preserve the geometric aspect ratio.'
foreach ($node in $document.SelectNodes('//*')) {
    Require ($node -eq $root -or $node.ParentNode -eq $root) 'Only the flat example structure is supported.'
    Require ($node.NamespaceURI -ceq $ns.LookupNamespace('s') -and $node.LocalName -cin @('svg','title','desc','rect','style','polygon','text','line','polyline','path')) 'Unsupported SVG element.'
    foreach ($attribute in $node.Attributes) {
        Require ($attribute.LocalName -notmatch '^(on|href$|style$|transform$|display$|visibility$|opacity$|fill-opacity$|stroke-opacity$|clip-path$|mask$|filter$)' -and $attribute.Value -notmatch 'url\s*\(') 'Active, hidden, transformed or referenced content is not allowed.'
    }
}
Require ($document.SelectNodes('//processing-instruction()').Count -eq 0) 'External XML processing instructions are not allowed.'
$style = @($document.SelectNodes('/s:svg/s:style', $ns))
# A fixed, tiny stylesheet avoids pretending to implement CSS visibility or external-resource resolution.
Require ($style.Count -eq 1 -and $style[0].InnerText -ceq 'text { fill: #282b30; font-family: Arial, Helvetica, sans-serif; font-size: 22px; }') 'Unexpected text stylesheet.'
$polygons = @($document.SelectNodes('//s:polygon', $ns)); $rays = @($document.SelectNodes('//s:polyline', $ns))
$labels = @($document.SelectNodes('//s:text', $ns)); $incoming = @($document.SelectNodes('//s:line', $ns))
Require ($polygons.Count -eq 1 -and $rays.Count -eq 6 -and $labels.Count -eq 3 -and $incoming.Count -eq 1) 'Expected one prism, six rays, three labels and one incoming line.'
Require ((@($labels | ForEach-Object InnerText | Sort-Object) -join '|') -ceq '404.7 nm|656.3 nm|N-BK7') 'Unexpected visible labels.'
foreach ($label in $labels) {
    Require ($label.ParentNode -eq $root -and $label.ChildNodes.Count -eq 1) 'Labels must be direct plain text.'
    Require (@($label.Attributes | Where-Object LocalName -NotIn @('x','y','text-anchor')).Count -eq 0) 'Unexpected label visibility override.'
    Require ((Number $label.x) -gt 0 -and (Number $label.x) -lt 1200 -and (Number $label.y) -gt 22 -and (Number $label.y) -lt 580) 'Label is outside the viewport.'
}
$vertices = Points $polygons[0] 3
$sides = @(Distance $vertices[0] $vertices[1]; Distance $vertices[1] $vertices[2]; Distance $vertices[2] $vertices[0])
Require (($sides | Measure-Object -Minimum).Minimum -gt 100 -and ($sides | Measure-Object -Maximum).Maximum - ($sides | Measure-Object -Minimum).Minimum -lt 0.002) 'Prism is not equilateral.'
Require ([Math]::Abs($vertices[0][1] - $vertices[1][1]) -le 0.001 -and $vertices[0][0] -lt $vertices[1][0] -and $vertices[2][1] -lt $vertices[0][1]) 'Expected upright prism with a horizontal base.'
$entry = (Points $rays[0] 3)[0]
$start = @((Number $incoming[0].x1), (Number $incoming[0].y1)); $end = @((Number $incoming[0].x2), (Number $incoming[0].y2))
foreach ($point in @($start, $end)) { Require ($point[0] -ge 0 -and $point[0] -le 1200 -and $point[1] -ge 0 -and $point[1] -le 580) 'Incoming ray is outside the viewport.' }
Require ((Distance $end $entry) -le 0.002 -and [Math]::Abs((Angle $start $end) - 15) -lt 0.001) 'Incoming ray must meet the shared entry at +15 degrees.'
OnFace $entry $vertices[0] $vertices[2]
$wavelengths = @(404.7, 435.8, 486.1, 546.1, 587.6, 656.3)
$colors = @('#794c9b','#526caa','#238994','#497d44','#af791c','#b64c3e')
$radians = [Math]::PI / 180; $maximumError = 0.0
for ($i = 0; $i -lt 6; $i++) {
    $points = Points $rays[$i] 3
    Require ($rays[$i].GetAttribute('stroke') -ceq $colors[$i] -and (Distance $points[0] $entry) -le 0.002) 'Ray identity or common entry changed.'
    OnFace $points[1] $vertices[1] $vertices[2]
    Require ((Distance $points[0] $points[1]) -ge 100 -and (Distance $points[1] $points[2]) -ge 100) 'Ray segments are too short for the angular tolerance.'
    $square = [Math]::Pow($wavelengths[$i] / 1000, 2)
    $index = [Math]::Sqrt(1 + 1.039612120 * $square / ($square - 0.006000699) + 0.231792344 * $square / ($square - 0.0200179144) + 1.010469450 * $square / ($square - 103.56065300))
    $refraction = [Math]::Asin([Math]::Sin(45 * $radians) / $index)
    $expected = 30 - [Math]::Asin($index * [Math]::Sin(60 * $radians - $refraction)) / $radians
    $angleError = [Math]::Abs((Angle $points[1] $points[2]) - $expected)
    # 0.001 px serialization contributes <0.00082 deg at >=100 px; float calculations fit the remaining margin.
    Require ($angleError -lt 0.001 -and [Math]::Abs((Angle $points[0] $points[1]) - ($refraction / $radians - 30)) -lt 0.001) "Snell angle mismatch at $($wavelengths[$i]) nm."
    $maximumError = [Math]::Max($maximumError, $angleError)
}
Write-Output ('Prism SVG verified; maximum exit-angle error: {0} deg.' -f $maximumError.ToString('F6', [Globalization.CultureInfo]::InvariantCulture))
