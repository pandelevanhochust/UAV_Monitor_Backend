# run-minikube.ps1
# Deploy the UAV Monitor Backend directly to Minikube on Windows

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "UAV Monitor Backend - Windows Minikube Orchestration   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Verify Minikube status on Windows host
Write-Host "`n[1/5] Checking Minikube status..." -ForegroundColor Yellow
$minikubeStatus = minikube status --format="{{.Host}}" 2>$null
if ($minikubeStatus -ne "Running") {
    Write-Host "⏳ Minikube is not running. Launching cluster with hardware safety caps..." -ForegroundColor DarkYellow
    # Caps memory to 6GB and uses 4 cores to prevent Windows laptop lag
    minikube start --driver=docker --memory=6144 --cpus=4
} else {
    Write-Host "✅ Minikube cluster active and responding on Windows host." -ForegroundColor Green
}

# 2. Sync Terminal Environment with Minikube's Internal Docker Daemon
# This is the vital trick: tells Windows to build images directly inside Minikube's brain
Write-Host "`n[2/5] Pointing PowerShell environment into Minikube Registry..." -ForegroundColor Yellow
minikube docker-env --shell powershell | Invoke-Expression

# 3. Build/Verify images directly inside Minikube's engine
# Kept commented to save processing time, uncomment if you need automated rebuilds
Write-Host "`n[3/5] Skipping inline image builds (Using local minikube cached images)..." -ForegroundColor Yellow
# docker compose build

# 4. Appending Architecture Layer Manifests
Write-Host "`n[4/5] Transmitting Kubernetes Configuration Layers..." -ForegroundColor Yellow

Write-Host "  Applying Namespaces & Global Variable Sheets..." -ForegroundColor Gray
kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/01-secrets.yaml
kubectl apply -f k8s/02-configmaps.yaml

Write-Host "  Provisioning Core Data Engines (Postgres, ClickHouse, Redis, RabbitMQ)..." -ForegroundColor Gray
# CRITICAL REMINDER: Ensure k8s/infra files DO NOT contain strict OCI Node-1 scheduling rules!
kubectl apply -f k8s/infra/

Write-Host "⏳ Allowing data fabrics 20s to allocate local drive tables..." -ForegroundColor Magenta
Start-Sleep -Seconds 20

Write-Host "  Deploying Kong API Gateway..." -ForegroundColor Gray
kubectl apply -f k8s/gateway/

Write-Host "  Deploying Core .NET Microservice Components..." -ForegroundColor Gray
# CRITICAL REMINDER: Ensure k8s/services files DO NOT contain Node-2 affinity blocks!
kubectl apply -f k8s/services/

Write-Host "  Deploying Frontend UI..." -ForegroundColor Gray
kubectl apply -f k8s/frontend/

Write-Host "  Deploying High-Availability Policies & Guardrails..." -ForegroundColor Gray
kubectl apply -f k8s/ha/

# 5. Success Tracking & Access Management
Write-Host "`n[5/5] Local Windows Deployment Complete!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Monitor your local drone tracking cluster with:"
Write-Host "  kubectl get pods -n uav-system -w"
Write-Host ""
Write-Host "⚠️  IMPORTANT FOR ACCESSING ON WINDOWS:" -ForegroundColor Yellow
Write-Host "Because Minikube runs inside an isolated container, you must leave a"
Write-Host "separate PowerShell window open running the following network tunnel command:"
Write-Host "  ---->  minikube tunnel  <----" -ForegroundColor Cyan
Write-Host "This will immediately map Kong and your frontend straight to http://localhost"
Write-Host "========================================================" -ForegroundColor Cyan