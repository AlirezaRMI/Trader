#!/usr/bin/env bash
set -e

GITHUB_USERNAME="AlirezaRMI"
REPO_NAME="Trader"
GITHUB_TOKEN="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"

echo "🚀 Starting Trader Robot Full Installation ..."

echo "Updating and upgrading system packages..."
sudo apt-get update
sudo apt-get upgrade -y

echo "Installing dependencies (ufw, curl, git, docker)..."
sudo apt-get install -y ufw ca-certificates curl git docker.io

echo "Configuring firewall..."
sudo ufw allow ssh
sudo ufw allow 5005/tcp 
sudo ufw default deny incoming
sudo ufw default allow outgoing
echo "y" | sudo ufw enable
echo "Firewall configured. Status:"
sudo ufw status verbose

echo "Starting Docker service..."
sudo systemctl enable --now docker.service

REPO_URL="https://
${GITHUB_USERNAME}:${GITHUB_TOKEN}@github.com/${GITHUB_USERNAME}/${REPO_NAME}.git"
INSTALL_DIR="/opt/tbot"

if [ ! -d "$INSTALL_DIR" ]; then
    echo "Cloning Trader Robot repository..."
    sudo git clone "$REPO_URL" "$INSTALL_DIR"
else
    echo "Updating Trader Robot repository..."
    sudo git -C "$INSTALL_DIR" pull
fi

cd "$INSTALL_DIR"

echo "Building Trader Robot docker image..."
sudo docker build -t tbot-api:latest .

echo "Creating systemd service for Trader Robot..."

sudo tee /etc/systemd/system/tbot.service > /dev/null <<EOF
[Unit]
Description=Trader Robot Docker Container
After=docker.service
Requires=docker.service

[Service]
Restart=always
RestartSec=10s
ExecStartPre=-/usr/bin/docker stop tbot-container
ExecStartPre=-/usr/bin/docker rm tbot-container
ExecStart=/usr/bin/docker run --name tbot-container -p 5005:8080 tbot-api:latest
ExecStop=-/usr/bin/docker stop tbot-container

[Install]
WantedBy=multi-user.target
EOF

echo "Reloading systemd, enabling and starting Trader Robot service..."
sudo systemctl daemon-reload
sudo systemctl enable --now tbot.service

echo ""
echo "✅ Trader Robot installation completed successfully."
echo "   The bot is running and configured to start on boot."
echo "   You can check the logs with: sudo docker logs -f tbot-container"