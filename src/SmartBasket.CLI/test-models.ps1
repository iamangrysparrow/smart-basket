[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$models = @("qwen2.5-coder:1.5b", "gemma2:2b", "llama3.2:3b", "gemma3:4b")
$prompt = 'Extract JSON from receipt. ONLY JSON:
Shop: ASHAN
Date: 03.12.2024
- Milk 1L 89.99
- Bread 45.50
- Eggs 120.00
Total: 255.49

Format: {"shop":"","date":"YYYY-MM-DD","total":0,"items":[{"name":"","price":0}]}'

Write-Host "Testing Ollama models" -ForegroundColor Green

foreach ($model in $models) {
    Write-Host "`n=== $model ===" -ForegroundColor Cyan

    $body = @{
        model = $model
        prompt = $prompt
        stream = $false
        options = @{temperature = 0.1; num_predict = 300}
    } | ConvertTo-Json -Depth 3

    $times = @()
    for ($i = 1; $i -le 3; $i++) {
        try {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-RestMethod -Uri 'http://localhost:11434/api/generate' -Method Post -Body $body -ContentType 'application/json; charset=utf-8' -TimeoutSec 30
            $sw.Stop()
            $times += $sw.Elapsed.TotalSeconds

            if ($i -eq 1) {
                $text = $response.response -replace "`n", " " -replace "`r", ""
                $preview = if ($text.Length -gt 120) { $text.Substring(0, 120) + "..." } else { $text }
                Write-Host "  Response: $preview"
            }
            Write-Host "  Run $i : $([math]::Round($sw.Elapsed.TotalSeconds, 2))s ($($response.eval_count) tok)"
        }
        catch {
            Write-Host "  Run $i : ERROR" -ForegroundColor Red
        }
    }

    if ($times.Count -gt 0) {
        $avg = [math]::Round(($times | Measure-Object -Average).Average, 2)
        Write-Host "  >>> AVG: ${avg}s <<<" -ForegroundColor Yellow
    }
}
