param(
    [Parameter(Mandatory = $true)] [string] $InputPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Bitmap]::FromFile($InputPath)
$width = $source.Width
$height = $source.Height
$output = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

for ($y = 0; $y -lt $height; $y++) {
    for ($x = 0; $x -lt $width; $x++) {
        $color = $source.GetPixel($x, $y)
        $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
        $minimum = [Math]::Min($color.R, [Math]::Min($color.G, $color.B))
        $luminance = (0.2126 * $color.R) + (0.7152 * $color.G) + (0.0722 * $color.B)

        # 생성된 체크무늬 두 색은 모두 매우 밝은 무채색이다. 소품은 어두운 석재·목재라
        # 밝기 205 아래를 그대로 두면 닫힌 내부와 회색 재질을 안전하게 보존할 수 있다.
        if ($luminance -ge 205 -and ($maximum - $minimum) -le 20) {
            $output.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
        }
        else {
            $output.SetPixel($x, $y, $color)
        }
    }
}

$output.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$source.Dispose()
$output.Dispose()
