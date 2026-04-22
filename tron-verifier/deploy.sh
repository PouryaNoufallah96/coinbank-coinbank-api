set -e

echo "🛑 Stopping container..."
docker compose down || true

echo "🚀 Building & starting..."
docker compose up -d --build

echo "📦 Container status:"
docker compose ps

echo "📜 Logs (Ctrl+C to exit):"
docker compose logs -f
