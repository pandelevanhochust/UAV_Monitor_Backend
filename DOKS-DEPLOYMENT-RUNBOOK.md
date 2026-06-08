# UAV Detection System — HA/DR/LB Demo & Operational Runbook

## Overview

This Kubernetes deployment showcases **High Availability (HA)**, **Disaster Recovery (DR)**, and **Load Balancing (LB)** patterns across microservices, stateful services, and databases.

### Key HA/DR/LB Features

| Feature                    | Implementation                                                 |
| -------------------------- | -------------------------------------------------------------- |
| **Multi-replica deployments** | All services run 3+ pods (min 4 for IngestionService)          |
| **Pod Disruption Budgets** | Enforce minimum pod availability during node drains/evictions  |
| **Anti-affinity rules**    | Pods spread across nodes (prefer different hosts)              |
| **Horizontal Pod Autoscaling** | CPU/memory-based scaling (3-20 replicas depending on service) |
| **Headless services**      | DNS-round-robin load balancing inside cluster                 |
| **Kong LoadBalancer**      | Exposes gateway as external LoadBalancer (gets public IP/ELB) |
| **StatefulSets + PVCs**    | RabbitMQ & ClickHouse with persistent data + clustering       |
| **Network Policies**       | Least-privilege inter-service communication                    |
| **Postgres read replicas** | Delegate to managed DO Postgres (standby replication)          |
| **Redis Sentinel/Cluster** | Delegate to managed DO Redis (with failover)                   |

---

## Pre-Deployment Checklist

### Prerequisites

1. **DigitalOcean DOKS cluster** (3+ nodes, 2+ vCPU, 4GB RAM each)
2. **doctl CLI** installed and authenticated: `doctl auth init --access-token YOUR_TOKEN`
3. **kubectl** configured to point to your DOKS cluster
4. **Helm 3** for optional helm chart deployments
5. **DO Container Registry** (DOCR) for private image storage

### Quick Setup

```bash
# 1. Create DOKS cluster (3 nodes, s-2vcpu-4gb)
doctl kubernetes cluster create uav-cluster \
  --region nyc3 \
  --size s-2vcpu-4gb \
  --count 3 \
  --auto-upgrade \
  --enable-cluster-autoscaling \
  --min-nodes 3 \
  --max-nodes 10

# 2. Get kubeconfig
doctl kubernetes cluster kubeconfig save uav-cluster
kubectl cluster-info

# 3. Create namespace
kubectl apply -f k8s/00-namespace-rbac.yaml

# 4. Create container registry (if not exists)
doctl registry create my-registry --region nyc3

# 5. Build and push all service images
for svc in userservice deviceservice ingestionservice logservice alertservice; do
  docker build -t my-registry/$svc:latest src/$svc/
  docker push registry.digitalocean.com/my-registry/$svc:latest
done
```

---

## Deployment Steps

### Step 1: Update Secrets + Image Registry Paths

Edit these placeholders in manifest files:

**In `k8s/02-secrets-config.yaml`:**
```yaml
POSTGRES_PASSWORD: "your-secure-password-here"
REDIS_PASSWORD: "your-redis-password"
RABBITMQ_DEFAULT_PASS: "your-rabbitmq-password"
CLICKHOUSE_PASSWORD: "your-clickhouse-password"
JWT_SECRET: "your-32-char-jwt-secret-key-1234"
```

**In all deployment files (`k8s/03-microservices-ha.yaml` and `k8s/04-statefulsets-ha.yaml`):**
Replace `REPLACE_REGISTRY` with your actual registry:
```bash
sed -i 's|REPLACE_REGISTRY|my-registry|g' k8s/*.yaml
```

### Step 2: Create Managed Databases (DigitalOcean)

```bash
# PostgreSQL with read replicas (for HA)
doctl databases create uav-postgres \
  --engine pg \
  --num-nodes 3 \
  --size db-s-1vcpu-1gb \
  --region nyc3 \
  --engine-version 16

# Add read replicas (optional but recommended for HA)
doctl databases replica create uav-postgres \
  --replica-name uav-postgres-read-1 \
  --region sfo3

# Redis (managed, handles failover automatically)
doctl databases create uav-redis \
  --engine redis \
  --num-nodes 3 \
  --size db-s-1vcpu-1gb \
  --region nyc3

# Get connection strings
doctl databases connection uav-postgres --format host,user,password,port,dbname

doctl databases connection uav-redis --format host,port,password
```

**Update secrets with managed DB endpoints:**
```bash
kubectl -n uav-system create secret generic uav-secrets \
  --from-literal=POSTGRES_HOST="dbaas-db-xxx.db.ondigitalocean.com" \
  --from-literal=POSTGRES_PASSWORD="your-password" \
  --from-literal=REDIS_HOST="redis-xxx.db.ondigitalocean.com" \
  --from-literal=REDIS_PASSWORD="your-password" \
  --dry-run=client -o yaml | kubectl apply -f -
```

### Step 3: Deploy Kubernetes Resources

```bash
# Apply all manifests in order
kubectl apply -f k8s/00-namespace-rbac.yaml
kubectl apply -f k8s/01-kong-lb.yaml
kubectl apply -f k8s/02-secrets-config.yaml
kubectl apply -f k8s/03-microservices-ha.yaml
kubectl apply -f k8s/04-statefulsets-ha.yaml
kubectl apply -f k8s/05-network-policies.yaml

# Wait for rollout
kubectl -n uav-system rollout status deployment/kong
kubectl -n uav-system rollout status deployment/userservice
kubectl -n uav-system rollout status statefulset/rabbitmq
kubectl -n uav-system rollout status statefulset/clickhouse
```

### Step 4: Get External LoadBalancer IP

```bash
# Kong is exposed as LoadBalancer — get the public IP/hostname
kubectl -n uav-system get svc kong

# Output:
# NAME   TYPE           CLUSTER-IP     EXTERNAL-IP     PORT(S)        AGE
# kong   LoadBalancer   10.245.x.x     192.0.2.100     80:30xxx/TCP   2m
```

Update your DNS or local `/etc/hosts` to point to the external IP.

---

## HA/DR/LB Demo Scenarios

### Demo 1: Pod Failover (Automatic Recovery)

**Scenario:** Kill one instance of UserService and observe automatic rescheduling.

```bash
# List pods
kubectl -n uav-system get pods -l app=userservice
# Output:
# NAME               READY   STATUS    RESTARTS   AGE
# userservice-abc    1/1     Running   0          5m
# userservice-def    1/1     Running   0          5m
# userservice-ghi    1/1     Running   0          5m

# Kill one pod (simulates node failure/crash)
kubectl -n uav-system delete pod userservice-abc

# Watch: Pod is immediately rescheduled
kubectl -n uav-system get pods -l app=userservice --watch

# Verify service still works — no downtime due to 3 replicas
curl -X GET http://KONG_IP/api/v1/health
```

**Result:** Service automatically recovers. DNS resolves to remaining 2 pods until new pod starts.

---

### Demo 2: Load Balancing Test

**Scenario:** Generate traffic and observe Kong distributing requests across service replicas.

```bash
# 1. Port-forward Kong locally (optional)
kubectl -n uav-system port-forward svc/kong 8080:80 &

# 2. Send 100 requests and check which pod handles them
for i in {1..100}; do
  curl -s http://localhost:8080/api/v1/health | jq .pod_name
done | sort | uniq -c

# Expected: Distribution across 3 pods

# 3. If using external IP directly:
for i in {1..100}; do
  curl -s http://EXTERNAL_IP/api/v1/health | jq .pod_name
done | sort | uniq -c
```

**Result:** Requests balanced across pod instances.

---

### Demo 3: Horizontal Pod Autoscaling

**Scenario:** Trigger CPU load and watch HPA scale up ingestionservice.

```bash
# 1. Check current replicas
kubectl -n uav-system get deployment ingestionservice
# Output: ingestionservice   4/4     4            4           5m

# 2. Generate telemetry load (mock high CPU)
kubectl -n uav-system run load-test --image=busybox \
  --rm -i -t -- /bin/sh

# Inside the pod:
for i in {1..500}; do
  curl -X POST http://kong:80/api/v1/telemetry/log \
    -H "X-Device-API-Key: test-key-123" \
    -H "Content-Type: application/json" \
    -d '{"device_id": 1001, "detected": true, "drone_type": "DJI"}' &
done

# 3. Monitor HPA scaling
kubectl -n uav-system get hpa --watch

# Expected: HPA detects high CPU → scales up to 6-8 replicas

# 4. Stop load, HPA scales back down (waits ~5 minutes)
```

**Result:** Horizontal scaling demonstrates elasticity.

---

### Demo 4: Graceful Shutdown (Pod Disruption Budget)

**Scenario:** Cordon a node and watch PDB prevent service disruption.

```bash
# 1. Get node names
kubectl get nodes

# 2. Mark node as unschedulable (simulates maintenance)
kubectl cordon <node-name>

# 3. Watch pods evict and reschedule to other nodes
kubectl -n uav-system get pods --watch

# 4. Verify PDB respected: alertservice PDB has minAvailable=2
#    So at most 1 alertservice pod evicts at a time
kubectl -n uav-system get pdb

# 5. Traffic is never dropped due to PDB constraints
curl -X GET http://KONG_IP/ws/alerts  # Still works

# 6. Uncordon node when ready
kubectl uncordon <node-name>
```

**Result:** Zero-downtime maintenance enabled by PDB.

---

### Demo 5: Database Failover (Managed Postgres)

**Scenario:** DO managed Postgres failover from primary to read replica.

```bash
# 1. Check current primary node
doctl databases describe uav-postgres --format "id,name,engine,status,num_nodes,version"

# 2. Trigger failover via do database (automatic on primary failure)
# In DigitalOcean console: Databases → uav-postgres → Settings → High Availability

# 3. Services continue working during failover (~30s outage if unconnected)
while true; do
  curl -X GET http://KONG_IP/api/v1/devices 2>&1 | grep -q "200" && echo "OK" || echo "FAIL"
  sleep 1
done

# 4. Verify connection pool recovered
kubectl -n uav-system logs deployment/deviceservice | tail -20
```

**Result:** Automatic failover with minimal downtime (connection pool recovers).

---

### Demo 6: StatefulSet Failover (RabbitMQ Cluster)

**Scenario:** Delete one RabbitMQ pod and watch cluster rebalance.

```bash
# 1. Check RabbitMQ cluster status
kubectl -n uav-system exec rabbitmq-0 -- \
  rabbitmq-diagnostics cluster_status

# 2. Delete one pod
kubectl -n uav-system delete pod rabbitmq-1

# 3. StatefulSet automatically creates rabbitmq-1 with same persistent volume
kubectl -n uav-system get statefulset rabbitmq
kubectl -n uav-system get pvc | grep rabbitmq

# 4. Cluster rebalances
kubectl -n uav-system exec rabbitmq-0 -- \
  rabbitmq-diagnostics cluster_status

# Expected: rabbitmq-1 rejoins cluster, messages/queues preserved via PVC
```

**Result:** Stateful service recovers without data loss.

---

### Demo 7: Network Policy Validation (Security)

**Scenario:** Verify network policies prevent unauthorized traffic.

```bash
# 1. Try to connect from unsanctioned pod to postgres (should fail)
kubectl -n uav-system run debug-pod --image=busybox --rm -i -t -- /bin/sh

# Inside pod:
nc -zv postgres 5432
# Expected: Connection refused (network policy blocks)

# 2. Try from deviceservice (allowed)
kubectl -n uav-system exec deviceservice-abc -- \
  nc -zv postgres 5432
# Expected: Connection open (allowed)
```

**Result:** Network policies enforce least-privilege communication.

---

### Demo 8: Rolling Update (No Downtime)

**Scenario:** Update service image and trigger rolling deployment.

```bash
# 1. Check current image
kubectl -n uav-system get deployment userservice -o jsonpath='{.spec.template.spec.containers[0].image}'

# 2. Trigger rolling update
kubectl -n uav-system set image deployment/userservice \
  userservice=registry.digitalocean.com/my-registry/userservice:v2.0

# 3. Watch rolling update (maxUnavailable: 0, so always at least 2 pods available)
kubectl -n uav-system rollout status deployment/userservice --watch

# 4. Traffic continues throughout update
curl -X GET http://KONG_IP/api/v1/health  # Never fails
```

**Result:** Zero-downtime rolling updates.

---

## Operational Runbook

### Health Checks

```bash
# Check all pods are running
kubectl -n uav-system get pods --watch

# Check deployments ready
kubectl -n uav-system get deployments

# Check statefulsets ready
kubectl -n uav-system get statefulsets

# Check HPA status
kubectl -n uav-system get hpa -o wide

# Check external IP assigned to Kong
kubectl -n uav-system get svc kong
```

### Troubleshooting

**Pods stuck in Pending:**
```bash
kubectl -n uav-system describe pod <pod-name>
kubectl top nodes  # Check node resources
kubectl autoscale deployment kong --min=2 --max=5  # Scale nodes if needed
```

**Service not responding:**
```bash
# Check endpoint discovery
kubectl -n uav-system get endpoints kong
kubectl -n uav-system get endpoints userservice

# Check network policies
kubectl -n uav-system get networkpolicy

# Verify pod logs
kubectl -n uav-system logs deployment/userservice --tail=50
```

**Database connection timeout:**
```bash
# Verify secrets
kubectl -n uav-system get secret uav-secrets -o yaml | grep -i postgres

# Test connectivity from a debug pod
kubectl -n uav-system run debug --image=postgres:16 --rm -i -t -- \
  psql -h POSTGRES_HOST -U POSTGRES_USER -d POSTGRES_DB
```

### Backup & Restore

```bash
# Backup RabbitMQ definitions
kubectl -n uav-system exec rabbitmq-0 -- \
  rabbitmqctl export_definitions /tmp/definitions.json

# Backup ClickHouse data (snapshot)
kubectl -n uav-system exec clickhouse-0 -- \
  clickhouse-client --query "CREATE TABLE radar_logs_backup AS SELECT * FROM radar_logs"

# Backup PVC data (DigitalOcean snapshots)
doctl compute volume-action snapshot <volume-id> \
  --snapshot-name uav-backup-$(date +%s)
```

---

## Cost Optimization (Future)

- **Reserved nodes:** Pre-reserve nodes for steady-state load
- **Spot instances:** Use for non-critical batch jobs (logs cleanup, analytics)
- **Storage tiers:** Archive old ClickHouse data to DO Spaces (S3-compatible)
- **Cluster autoscaling:** Scale node count based on pending pods

---

## Monitoring & Observability Setup (Recommended)

Add Prometheus + Grafana for production:

```bash
# Install via Helm
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm install prometheus prometheus-community/kube-prometheus-stack -n uav-system

# Dashboards:
# - Pod CPU/Memory utilization
# - Request latency (Kong)
# - RabbitMQ queue depth
# - ClickHouse disk usage
```

---

## Next Steps

1. Deploy via `kubectl apply -f k8s/`
2. Run demo scenarios to verify HA/DR/LB
3. Set up alerting rules (Prometheus)
4. Document runbooks for your ops team
5. Practice failover drills monthly

