# Screenshots

All PNGs are rendered from HTML source files using puppeteer (headless Chrome, 2× DPI).

| PNG | Source HTML | Shows |
|-----|-------------|-------|
| `capability-report.png` | `capability-report.html` | End-to-end flow, running services, live match results, test suite (9 .NET + 3 Go), CI pipeline |
| `swagger-api.png` | `swagger-api.html` | API surface: all 5 endpoints + CreateSubmissionRequest schema |
| `matched-result-json.png` | `matched-result.html` | Full request→response cycle with syntax-highlighted JSON, confidence bar, worker path notes |

## Regenerating

```bash
node -e "
const puppeteer = require('/tmp/node_modules/puppeteer');
(async () => {
  const browser = await puppeteer.launch({args:['--no-sandbox','--disable-gpu']});
  const page = await browser.newPage();

  await page.setViewport({width:1200,height:800,deviceScaleFactor:2});
  await page.goto('file://\$(pwd)/capability-report.html',{waitUntil:'networkidle0'});
  await page.screenshot({path:'capability-report.png',fullPage:true});

  await page.setViewport({width:1280,height:800,deviceScaleFactor:2});
  await page.goto('file://\$(pwd)/matched-result.html',{waitUntil:'networkidle0'});
  await page.screenshot({path:'matched-result-json.png',fullPage:true});

  await page.setViewport({width:1060,height:600,deviceScaleFactor:2});
  await page.goto('file://\$(pwd)/swagger-api.html',{waitUntil:'networkidle0'});
  await page.screenshot({path:'swagger-api.png',fullPage:true});

  await browser.close();
})();
"
```
