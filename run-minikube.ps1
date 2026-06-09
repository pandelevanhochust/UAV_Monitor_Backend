# run-minikube.ps1
# Deploy the UAV Monitor Backend to a local Minikube cluster

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "UAV Monitor Backend - Minikube Local Deployment" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Start Minikube with optimized resources
Write-Host "`n[1/5] Starting Minikube cluster..." -ForegroundColor Yellow
# NOTE: Set disk-size to 8g. Setting it exactly to 5g causes immediate Kubernetes
# DiskPressure crashes because the base OS + our 6 Docker images exceed 5GB.
minikube start --cpus 4 --memory 8192 --disk-size 8g

# 2. Point local Docker CLI to Minikube's Docker daemon
Write-Host "`n[2/5] Connecting to Minikube Docker environment..." -ForegroundColor Yellow
& minikube -p minikube docker-env | Invoke-Expression

# 3. Build Docker images directly inside Minikube
Write-Host "`n[3/5] Building Docker images inside cluster..." -ForegroundColor Yellow
docker compose build

# 4. Apply Kubernetes Manifests
Write-Host "`n[4/5] Applying Kubernetes Manifests..." -ForegroundColor Yellow

Write-Host "  Applying Namespace & Configs..."
kubectl apply -f k8s/00-namespace.yaml
kubectl apply -f k8s/01-secrets.yaml
kubectl apply -f k8s/02-configmaps.yaml

Write-Host "  Applying Infrastructure (Postgres, ClickHouse, Redis, RabbitMQ)..."
kubectl apply -f k8s/infra/

Write-Host "  Waiting for databases to initialize (up to 2 mins)..." -ForegroundColor Magenta
kubectl wait --namespace uav-system --for=condition=ready pod -l app=postgres --timeout=120s
kubectl wait --namespace uav-system --for=condition=ready pod -l app=clickhouse --timeout=120s

Write-Host "  Applying API Gateway (Kong)..."
kubectl apply -f k8s/gateway/

Write-Host "  Applying Microservices..."
kubectl apply -f k8s/services/

Write-Host "  Applying Frontend..."
kubectl apply -f k8s/frontend/

Write-Host "  Applying HA Policies (PDBs & HPA)..."
kubectl apply -f k8s/ha/

# 5. Success info & Port Forwarding
Write-Host "`n[5/5] Deployment Complete!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Monitor your deployment status:"
Write-Host "  kubectl get pods -n uav-system -o wide -w"
Write-Host ""
Write-Host "To allow your Vercel frontend to reach the backend, you MUST run this command"
Write-Host "in a separate terminal window and leave it running:" -ForegroundColor Yellow
Write-Host "  kubectl port-forward svc/kong 8880:80 -n uav-system" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
