#!/usr/bin/env node
import { execSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const frontendDir = resolve(__dirname, '..');
const rootDir = resolve(frontendDir, '..');
const apiProjectDir = resolve(rootDir, 'backend', 'src', 'G54.Api');
const localSwaggerFile = resolve(frontendDir, 'swagger.json');
const swaggerUrl = process.env.SWAGGER_URL || 'http://localhost:5000/swagger/v1/swagger.json';

console.log('==> [1/3] Chuẩn bị tài liệu Swagger / OpenAPI...');

let obtainedSwagger = false;

// 1. Kiểm tra xem Backend có đang chạy để lấy trực tiếp swagger.json không
try {
  const response = await fetch(swaggerUrl, { signal: AbortSignal.timeout(2000) });
  if (response.ok) {
    const jsonText = await response.text();
    // Validate JSON format
    JSON.parse(jsonText);
    writeFileSync(localSwaggerFile, jsonText, 'utf8');
    console.log(`    ✓ Đã tải thành công swagger.json mới nhất từ ${swaggerUrl}`);
    obtainedSwagger = true;
  }
} catch {
  // Backend chưa chạy, tiếp tục thử xuất tĩnh qua .NET
}

// 2. Nếu backend chưa chạy, thử xuất trực tiếp qua dotnet CLI
if (!obtainedSwagger) {
  try {
    console.log('    • Backend chưa bật tại port 5000, đang thử xuất swagger từ mã nguồn .NET...');
    const env = {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      PATH: `${process.env.PATH || ''};${process.env.USERPROFILE || ''}\\.dotnet\\tools`,
    };

    // Đảm bảo assembly đã build
    execSync('dotnet build --no-restore -c Debug', { cwd: apiProjectDir, stdio: 'pipe' });

    // Chạy swagger tofile
    execSync(`swagger tofile --output "${localSwaggerFile}" bin/Debug/net8.0/G54.Api.dll v1`, {
      cwd: apiProjectDir,
      env,
      stdio: 'pipe',
    });

    if (existsSync(localSwaggerFile)) {
      console.log('    ✓ Đã xuất thành công swagger.json trực tiếp từ G54.Api.dll');
      obtainedSwagger = true;
    }
  } catch (err) {
    // Nếu chưa cài swagger global tool, tiếp tục kiểm tra file local có sẵn
  }
}

// 3. Fallback: Sử dụng swagger.json đã lưu trữ sẵn trong frontend
if (!obtainedSwagger) {
  if (existsSync(localSwaggerFile)) {
    console.log('    ✓ Sử dụng tài liệu swagger.json hiện hữu trong thư mục frontend.');
  } else {
    console.error('    ✗ Lỗi: Không thể tìm thấy hoặc sinh file swagger.json.');
    process.exit(1);
  }
}

// 4. Sinh TypeScript Angular Client qua NSwag
console.log('==> [2/3] Đang sinh Angular API Client qua NSwag...');
try {
  execSync('npx nswag run nswag.json', { cwd: frontendDir, stdio: 'inherit' });
  console.log('    ✓ NSwag đã sinh xong: src/app/core/api/api-client.ts');
} catch (error) {
  console.error('    ✗ Lỗi khi chạy NSwag:', error);
  process.exit(1);
}

// 5. Định dạng mã nguồn tự động qua Prettier
console.log('==> [3/3] Định dạng mã nguồn tự sinh với Prettier...');
try {
  execSync('npx prettier --write src/app/core/api/api-client.ts', {
    cwd: frontendDir,
    stdio: 'inherit',
  });
  console.log('    ✓ Hoàn tất!');
} catch {
  // Prettier cảnh báo không block kết quả
}

console.log('\n🎉 Hoàn thành sinh API Client từ Swagger thành công!');
console.log('   File sinh: frontend/src/app/core/api/api-client.ts');
