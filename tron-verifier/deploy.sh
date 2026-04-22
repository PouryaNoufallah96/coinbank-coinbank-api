#!/bin/bash
set -e

IMAGE_NAME="tron-verifier"
CONTAINER_NAME="tron-verifier"
COMPOSE_FILE="docker-compose.yml"

echo " Stopping old containers..."
docker compose -f $COMPOSE_FILE down || true

echo " Removing old image..."
docker rmi $IMAGE_NAME || true

echo "Building Docker image..."
docker build -t $IMAGE_NAME .

echo "Starting container..."
docker compose -f $COMPOSE_FILE up -d --build

echo " Container status:"
docker compose -f $COMPOSE_FILE ps

echo "📜 Showing logs (Ctrl+C to exit)..."
docker compose -f $COMPOSE_FILE logs -f