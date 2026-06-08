# UAV Detection System — DOKS Quick Reference Card

## Essential Commands

### Cluster & Connectivity

```bash
# Get cluster info
kubectl cluster-info

# Get Kong external IP
kubectl -n uav-system get svc kong -o wide

# Port-forward Kong locally (for testing)
kubectl -n uav-system port-forward svc/kong 8080:80

# Port-forward to specific service
kubectl -n uav-system port-forward svc/userservice 8080:8080
```

### Pod Management

```bash
# List all pods
kubectl -n uav-system get pods

# Watch pods
kubectl -n uav-system get pods --watch

# Get pod details
kubectl -n uav-system describe pod <pod-name>

# Check pod logs
kubectl -n uav-system logs <pod-name>
kubectl -n uav-system logs <pod-name> -c <container-name>

# Follow logs (tail -f equivalent)
kubectl -n uav-system logs -f <pod-name>

# Logs from all pods in a deployment
kubectl -n uav-system logs -f deployment/userservice

# Kill a pod (simulates failure)
kubectl -n uav-system delete pod <pod-name>

# Enter a pod (like docker exec)
kubectl -n uav-system exec -it <pod-name> -- /bin/sh
```

### Deployment Management

```bash
# List deployments
kubectl -n uav-system get deployments

# Check rollout status
kubectl -n uav-system rollout status deployment/userservice

# Watch rollout
kubectl -n uav-system rollout status deployment/userservice --watch

# Update image
kubectl -n uav-system set image deployment/userservice \
  userservice=registry.digitalocean.com/my-registry/userservice:v2.0

# Scale replicas
kubectl -n uav-system scale deployment userservice --replicas 5

# Check deployment history
kubectl -n uav-system rollout history deployment/userservice

# Rollback to previous version
kubectl -n uav-system rollout undo deployment/userservice
```

### StatefulSet Management

```bash
# List statefulsets
kubectl -n uav-system get statefulset

# Check PVC (persistent volumes)
kubectl -n uav-system get pvc

# Check PV (persistent volumes)
kubectl get pv

# Exec into specific statefulset pod
kubectl -n uav-system exec -it rabbitmq-0 -- /bin/sh

# RabbitMQ cluster status
kubectl -n uav-system exec rabbitmq-0 -- \
  rabbitmq-diagnostics cluster_status

# ClickHouse health check
kubectl -n uav-system exec clickhouse-0 -- \
  clickhouse-client --query "SELECT 1"
```

### Horizontal Pod Autoscaler (HPA)

```bash
# List HPA
kubectl -n uav-system get hpa

# Watch HPA scaling
kubectl -n uav-system get hpa --watch

# Check HPA metrics
kubectl -n uav-system get hpa -o wide

# Describe HPA
kubectl -n uav-system describe hpa userservice-hpa
```

### Pod Disruption Budget (PDB)

```bash
# List PDB
kubectl -n uav-system get pdb

# Check PDB constraints
kubectl -n uav-system describe pdb kong-pdb
```

### Network Policy

```bash
# List network policies
kubectl -n uav-system get networkpolicy

# Test connectivity (from debug pod)
kubectl -n uav-system run debug --image=busybox --rm -i -t -- /bin/sh

# Inside debug pod:
nc -zv servicename 8080  # Test connectivity
nslookup userservice     # Test DNS
```

### Secrets & ConfigMaps

```bash
# List secrets
kubectl -n uav-system get secret

# View secret (base64 decoded)
kubectl -n uav-system get secret uav-secrets -o jsonpath='{.data.POSTGRES_PASSWORD}' | base64 -d

# Edit secret
kubectl -n uav-system edit secret uav-secrets

# List ConfigMaps
kubectl -n uav-system get configmap

# View ConfigMap
kubectl -n uav-system get cm kong-config -o yaml
```

### Resource Management

```bash
# Check node resources
kubectl top nodes

# Check pod resource usage
kubectl -n uav-system top pods

# Check pod resource limits
kubectl -n uav-system describe pod <pod-name> | grep -A 5 "Limits\|Requests"
```

---

## Demo Scenarios (Copy-Paste)

### Demo 1: Pod Failover

```bash
# Terminal 1: Watch pods
kubectl -n uav-system get pods -l app=userservice --watch

# Terminal 2: Kill a pod
kubectl -n uav-system delete pod $(kubectl -n uav-system get pods -l app=userservice -o jsonpath='{.items[0].metadata.name}')

# Observe: New pod created immediately
```

### Demo 2: Load Balancing

```bash
# Terminal 1: Port-forward Kong
kubectl -n uav-system port-forward svc/kong 8080:80

# Terminal 2: Send repeated requests and check pod distribution
for i in {1..50}; do
  curl -s http://localhost:8080/api/v1/health 2>&1 | head -1
done | sort | uniq -c
```

### Demo 3: HPA Scaling (Generate CPU load)

```bash
# Terminal 1: Watch HPA
kubectl -n uav-system get hpa ingestionservice-hpa --watch

# Terminal 2: Generate load (inside a pod)
kubectl -n uav-system exec -it $(kubectl -n uav-system get pods -l app=ingestionservice -o jsonpath='{.items[0].metadata.name}') -- /bin/sh

# Inside pod:
yes > /dev/null  # Max out one CPU

# Observe: CPU usage climbs, HPA triggers scale-up

# Press Ctrl+C to stop load, HPA scales down after ~5 minutes
```

### Demo 4: PDB Enforcement (Cordon a node)

```bash
# List nodes
kubectl get nodes

# Cordon a node (mark as unschedulable)
kubectl cordon <node-name>

# Watch pods evict and reschedule
kubectl -n uav-system get pods --watch

# Note: PDB minAvailable enforces safe eviction

# Uncordon when done
kubectl uncordon <node-name>
```

### Demo 5: Rolling Update

```bash
# Terminal 1: Watch pods
kubectl -n uav-system get pods -l app=userservice --watch

# Terminal 2: Update image
kubectl -n uav-system set image deployment/userservice \
  userservice=registry.digitalocean.com/my-registry/userservice:v2.0

# Observe: Pods terminate/restart in rolling fashion, no downtime

# Verify: curl http://KONG_IP/api/v1/health continues to work
```

### Demo 6: Database Failover (Managed Postgres)

```bash
# Terminal 1: Continuous health check
while true; do
  curl -s http://KONG_IP/api/v1/devices | head -1
  sleep 1
done

# Terminal 2: Trigger failover in DO console
# Databases → uav-postgres → Settings → Failover

# Observe: Brief interruption (~30s), then connections recover
```

---

## Troubleshooting Checklist

| Issue                        | Command to Debug                                                   |
| ---------------------------- | ------------------------------------------------------------------ |
| Pod stuck in Pending         | `kubectl describe pod <pod-name>`                                  |
| Pod crash loop               | `kubectl logs <pod-name> --previous`                               |
| Service DNS not resolving    | `nslookup userservice.uav-system.svc.cluster.local` (from pod)    |
| Connection refused           | Check NetworkPolicy: `kubectl get networkpolicy`                  |
| Out of resources             | `kubectl top nodes` + `kubectl top pods`                           |
| High latency                 | Check HPA: `kubectl get hpa` + check node CPU/memory              |
| RabbitMQ cluster unhealthy   | `kubectl exec rabbitmq-0 -- rabbitmq-diagnostics cluster_status` |
| ClickHouse disk full         | `kubectl exec clickhouse-0 -- du -sh /var/lib/clickhouse`         |

---

## Quick Health Check Script

```bash
#!/bin/bash
echo "=== UAV System Health Check ==="
echo ""

echo "1. Cluster:"
kubectl cluster-info

echo ""
echo "2. Nodes:"
kubectl top nodes

echo ""
echo "3. Pods:"
kubectl -n uav-system get pods

echo ""
echo "4. HPA:"
kubectl -n uav-system get hpa

echo ""
echo "5. PDB:"
kubectl -n uav-system get pdb

echo ""
echo "6. Kong External IP:"
kubectl -n uav-system get svc kong

echo ""
echo "7. StatefulSets:"
kubectl -n uav-system get statefulset

echo ""
echo "8. Network Connectivity (test from debug pod):"
kubectl -n uav-system run debug --image=busybox --rm -i -t -- /bin/sh -c "nc -zv userservice 8080"

echo ""
echo "=== Health Check Complete ==="
```

---

## Environment Variables & Secrets

**Stored in:** `k8s/02-secrets-config.yaml`

Key placeholders to replace:
- `POSTGRES_PASSWORD`
- `REDIS_PASSWORD`
- `RABBITMQ_DEFAULT_PASS`
- `CLICKHOUSE_PASSWORD`
- `JWT_SECRET` (32+ characters)

Update via:
```bash
kubectl -n uav-system create secret generic uav-secrets \
  --from-literal=POSTGRES_PASSWORD="your-password" \
  --dry-run=client -o yaml | kubectl apply -f -
```

---

## Performance Tuning

### Increase Replica Count

```bash
kubectl -n uav-system scale deployment ingestionservice --replicas 10
```

### Adjust HPA Min/Max

```bash
kubectl -n uav-system edit hpa userservice-hpa
# Change minReplicas: 3, maxReplicas: 20
```

### Increase Resource Limits

```bash
kubectl -n uav-system set resources deployment userservice \
  --limits=cpu=1000m,memory=1Gi \
  --requests=cpu=500m,memory=512Mi
```

---

## Logging & Monitoring

### View service logs

```bash
# UserService logs
kubectl -n uav-system logs -f deployment/userservice

# Kong logs
kubectl -n uav-system logs -f deployment/kong

# Last 100 lines
kubectl -n uav-system logs deployment/userservice --tail=100
```

### Install Prometheus + Grafana (optional)

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm install prometheus prometheus-community/kube-prometheus-stack -n uav-system
```

Access Grafana:
```bash
kubectl -n uav-system port-forward svc/prometheus-grafana 3000:80
# http://localhost:3000 (default: admin/prom-operator)
```

