#!/bin/bash
################################################################################
# UAV Detection System — DOKS Deployment Automation Script
# 
# Usage: ./doks-deploy.sh <cluster-name> <region> <registry-name> <image-tag>
# Example: ./doks-deploy.sh uav-cluster nyc3 my-registry latest
#
# Prerequisites:
#   - doctl CLI installed and authenticated
#   - kubectl installed
#   - Docker installed (for building images)
################################################################################

set -e

CLUSTER_NAME=${1:-uav-cluster}
REGION=${2:-nyc3}
REGISTRY_NAME=${3:-my-registry}
IMAGE_TAG=${4:-latest}
REGISTRY_URL="registry.digitalocean.com/$REGISTRY_NAME"

echo "=========================================="
echo "UAV Detection System — DOKS Deployment"
echo "=========================================="
echo "Cluster: $CLUSTER_NAME"
echo "Region: $REGION"
echo "Registry: $REGISTRY_URL"
echo "Image Tag: $IMAGE_TAG"
echo ""

# Step 1: Create DOKS cluster if it doesn't exist
echo "[1/8] Checking DOKS cluster..."
if ! doctl kubernetes cluster get $CLUSTER_NAME &>/dev/null; then
    echo "Creating DOKS cluster: $CLUSTER_NAME"
    doctl kubernetes cluster create $CLUSTER_NAME \
        --region $REGION \
        --size s-2vcpu-4gb \
        --count 3 \
        --auto-upgrade \
        --enable-cluster-autoscaling \
        --min-nodes 3 \
        --max-nodes 10 \
        --wait
fi
echo "✓ DOKS cluster ready"

# Step 2: Get kubeconfig
echo ""
echo "[2/8] Configuring kubectl..."
doctl kubernetes cluster kubeconfig save $CLUSTER_NAME
kubectl cluster-info
echo "✓ kubectl configured"

# Step 3: Create container registry if needed
echo ""
echo "[3/8] Setting up container registry..."
if ! doctl registry get $REGISTRY_NAME &>/dev/null; then
    echo "Creating container registry: $REGISTRY_NAME"
    doctl registry create $REGISTRY_NAME --region $REGION
fi
echo "✓ Container registry ready"

# Step 4: Build and push Docker images
echo ""
echo "[4/8] Building and pushing Docker images..."
SERVICES=(userservice deviceservice ingestionservice logservice alertservice)

for svc in "${SERVICES[@]}"; do
    echo "Building $svc..."
    docker build -t $REGISTRY_URL/$svc:$IMAGE_TAG \
        -f src/${svc^}/UavSystem.${svc^}.WebApi/Dockerfile .
    
    echo "Pushing $svc..."
    docker push $REGISTRY_URL/$svc:$IMAGE_TAG
done
echo "✓ All images pushed to registry"

# Step 5: Update manifests with actual registry
echo ""
echo "[5/8] Updating manifests..."
for file in k8s/*.yaml; do
    sed -i.bak "s|REPLACE_REGISTRY|$REGISTRY_NAME|g" "$file"
    rm -f "${file}.bak"
done
echo "✓ Manifests updated"

# Step 6: Create namespace and RBAC
echo ""
echo "[6/8] Creating namespace and RBAC..."
kubectl apply -f k8s/00-namespace-rbac.yaml
echo "✓ Namespace and RBAC created"

# Step 7: Create managed databases
echo ""
echo "[7/8] Creating managed databases (optional)..."
echo "Skipping for now. You can run these manually:"
echo ""
echo "  # Postgres"
echo "  doctl databases create uav-postgres --engine pg --num-nodes 3 --size db-s-1vcpu-1gb --region $REGION"
echo ""
echo "  # Redis"
echo "  doctl databases create uav-redis --engine redis --num-nodes 3 --size db-s-1vcpu-1gb --region $REGION"
echo ""
echo "Then update k8s/02-secrets-config.yaml with connection strings."
echo ""

# Step 8: Deploy all manifests
echo "[8/8] Deploying manifests to Kubernetes..."
kubectl apply -f k8s/01-kong-lb.yaml
kubectl apply -f k8s/02-secrets-config.yaml
kubectl apply -f k8s/03-microservices-ha.yaml
kubectl apply -f k8s/04-statefulsets-ha.yaml
kubectl apply -f k8s/05-network-policies.yaml

echo "✓ Manifests deployed"

# Wait for Kong LoadBalancer to get external IP
echo ""
echo "Waiting for Kong to get external IP (this may take 1-2 minutes)..."
EXTERNAL_IP=""
while [ -z $EXTERNAL_IP ]; do
    echo "Waiting for external IP..."
    EXTERNAL_IP=$(kubectl -n uav-system get svc kong --template="{{range .status.loadBalancer.ingress}}{{.ip}}{{end}}")
    [ -z "$EXTERNAL_IP" ] && sleep 10
done

echo ""
echo "=========================================="
echo "✓ Deployment Complete!"
echo "=========================================="
echo ""
echo "External IP (Kong): $EXTERNAL_IP"
echo ""
echo "Next steps:"
echo "1. Update /etc/hosts or DNS to point to $EXTERNAL_IP"
echo "2. Test: curl http://$EXTERNAL_IP/api/v1/health"
echo "3. Run demo scenarios from DOKS-DEPLOYMENT-RUNBOOK.md"
echo ""
echo "View logs:"
echo "  kubectl -n uav-system logs -f deployment/userservice"
echo ""
echo "Monitor pods:"
echo "  kubectl -n uav-system get pods --watch"
echo ""
