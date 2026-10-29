#!/usr/bin/env bash
#
# Applies every service's src/services/<Service>/deploy/k8s.yaml to the current kubectl context:
#
#   REGISTRY=registry.example.com TAG=1.4.0 deploy/k8s/apply.sh
#   REGISTRY=registry.example.com TAG=1.4.0 deploy/k8s/apply.sh --namespace shop
#
# Images come from deploy/build-images.sh with the same REGISTRY and TAG. Arguments go to kubectl apply.
set -euo pipefail

cd "$(dirname "$0")/../.."

: "${REGISTRY:?Set REGISTRY, the registry the images were pushed to}"
TAG="${TAG:-latest}"

for manifest in src/services/*/deploy/k8s.yaml; do
    [ -e "$manifest" ] || continue
    echo "==> $manifest"
    sed -e "s#__REGISTRY__#$REGISTRY#g" -e "s#__TAG__#$TAG#g" "$manifest" | kubectl apply "$@" -f -
done
