# Recreates the "Tiefer Puls" notification sound without external audio dependencies.
$ErrorActionPreference = 'Stop'
$outputPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/OTPinsert.Platform/Resources/NewCode.wav'
$sampleRate = 44100
$duration = 0.42
$sampleCount = [int]($sampleRate * $duration)
$samples = [double[]]::new($sampleCount)
$peak = 0.0
# E3 and B3 with a soft attack, warm harmonic, and a gently fading tail.
for ($index = 0; $index -lt $sampleCount; $index++) {
    $time = $index / [double]$sampleRate
    $sample = 0.0
    foreach ($note in @(@(164.81, 0.0), @(246.94, 0.07))) {
        $elapsed = $time - $note[1]
        if ($elapsed -ge 0) {
            $envelope = (1 - [Math]::Exp(-$elapsed / 0.012)) * [Math]::Exp(-12 * $elapsed)
            $phase = 2 * [Math]::PI * $note[0] * $elapsed
            $sample += $envelope * ([Math]::Sin($phase) + 0.15 * [Math]::Sin(2 * $phase) * [Math]::Exp(-8 * $elapsed))
        }
    }
    $sample *= [Math]::Min(1.0, ($duration - $time) / 0.09)
    $samples[$index] = $sample
    $peak = [Math]::Max($peak, [Math]::Abs($sample))
}
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($outputPath))
try {
    # Mono, 16-bit PCM WAV.
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $sampleCount * 2))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([int]16)
    $writer.Write([int16]1)
    $writer.Write([int16]1)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]($sampleRate * 2))
    $writer.Write([int16]2)
    $writer.Write([int16]16)
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]($sampleCount * 2))
    foreach ($sample in $samples) {
        $writer.Write([int16]([Math]::Round($sample / $peak * 0.4 * 32767)))
    }
}
finally {
    $writer.Dispose()
}
