#!/usr/bin/env python3
"""
Monitoring dashboard for the Stereo Vision System.
Provides real-time visualization of camera feeds, model predictions,
and system metrics.
"""

import os
import json
import asyncio
import uvicorn
import numpy as np
import pandas as pd
from pathlib import Path
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Any
from fastapi import FastAPI, WebSocket, WebSocketDisconnect, Request
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from fastapi.responses import HTMLResponse, JSONResponse
import plotly.graph_objects as go
from plotly.subplots import make_subplots

# Configuration
CONFIG = {
    "data_dir": "data/auto_collected",
    "models_dir": "models",
    "port": 8000,
    "update_interval": 1.0,  # seconds
}

app = FastAPI(title="Stereo Vision Monitoring")
app.mount("/static", StaticFiles(directory="static"), name="static")
templates = Jinja2Templates(directory="templates")

# Store active WebSocket connections
active_connections: List[WebSocket] = []

class MonitoringData:
    """Manages monitoring data and metrics."""
    
    def __init__(self, data_dir: str):
        self.data_dir = Path(data_dir)
        self.metrics = {
            'fps': [],
            'latency': [],
            'confidence': [],
            'memory_usage': [],
            'cpu_usage': [],
            'gpu_usage': [],
        }
        self.last_update = datetime.now()
        
    def update_metrics(self, metrics: Dict[str, float]):
        """Update metrics with new data."""
        now = datetime.now()
        time_diff = (now - self.last_update).total_seconds()
        
        # Keep only the last hour of data
        for key in self.metrics:
            if key in metrics:
                self.metrics[key].append((now, metrics[key]))
                
                # Remove data older than 1 hour
                self.metrics[key] = [ 
                    (ts, val) for ts, val in self.metrics[key] 
                    if (now - ts).total_seconds() <= 3600 
                ]
        
        self.last_update = now
    
    def get_metrics_dataframe(self) -> Dict[str, pd.DataFrame]:
        """Convert metrics to pandas DataFrames for plotting."""
        dfs = {}
        for metric, values in self.metrics.items():
            if values:
                times, vals = zip(*values)
                dfs[metric] = pd.DataFrame({
                    'time': times,
                    'value': vals
                })
        return dfs
    
    def get_system_stats(self) -> Dict[str, Any]:
        """Get current system statistics."""
        # This is a simplified version - in a real implementation, you'd get these from system monitoring
        return {
            'cpu_usage': 45.2,
            'memory_usage': 65.8,
            'gpu_usage': 32.1,
            'disk_usage': 28.4,
            'uptime': str(timedelta(seconds=3600 * 5 + 23 * 60 + 45))  # 5h 23m 45s
        }
    
    def get_model_info(self) -> List[Dict[str, Any]]:
        """Get information about available models."""
        models = []
        models_dir = Path(CONFIG["models_dir"])
        
        if models_dir.exists():
            for model_file in models_dir.glob("*.onnx"):
                stats = model_file.stat()
                models.append({
                    'name': model_file.name,
                    'size_mb': stats.st_size / (1024 * 1024),
                    'modified': datetime.fromtimestamp(stats.st_mtime).isoformat(),
                })
        
        return sorted(models, key=lambda x: x['modified'], reverse=True)
    
    def get_data_stats(self) -> Dict[str, Any]:
        """Get statistics about collected data."""
        data_dir = Path(CONFIG["data_dir"])
        stats = {
            'total_samples': 0,
            'samples_today': 0,
            'samples_by_day': {},
            'defect_stats': {},
        }
        
        today = datetime.now().date()
        
        for split in ['train', 'val']:
            left_dir = data_dir / split / 'left'
            if left_dir.exists():
                for img_file in left_dir.glob('*.png'):
                    # Count total samples
                    stats['total_samples'] += 1
                    
                    # Count samples by day
                    mtime = datetime.fromtimestamp(img_file.stat().st_mtime).date()
                    day_str = mtime.isoformat()
                    stats['samples_by_day'][day_str] = stats['samples_by_day'].get(day_str, 0) + 1
                    
                    # Count today's samples
                    if mtime == today:
                        stats['samples_today'] += 1
                    
                    # Check for corresponding label file
                    label_file = data_dir / split / 'labels' / f"{img_file.stem}.json"
                    if label_file.exists():
                        try:
                            with open(label_file, 'r') as f:
                                label = json.load(f)
                                if 'defects' in label:
                                    for defect in label['defects']:
                                        defect_type = defect.get('type', 'unknown')
                                        stats['defect_stats'][defect_type] = stats['defect_stats'].get(defect_type, 0) + 1
                        except Exception as e:
                            print(f"Error reading {label_file}: {e}")
        
        return stats

# Initialize monitoring data
monitoring_data = MonitoringData(CONFIG["data_dir"])

# WebSocket manager
class ConnectionManager:
    def __init__(self):
        self.active_connections: List[WebSocket] = []

    async def connect(self, websocket: WebSocket):
        await websocket.accept()
        self.active_connections.append(websocket)

    def disconnect(self, websocket: WebSocket):
        self.active_connections.remove(websocket)

    async def broadcast(self, message: str):
        for connection in self.active_connections:
            try:
                await connection.send_text(message)
            except Exception as e:
                print(f"Error sending message: {e}")

manager = ConnectionManager()

# API Endpoints
@app.get("/", response_class=HTMLResponse)
async def dashboard(request: Request):
    """Render the main dashboard."""
    return templates.TemplateResponse("dashboard.html", {"request": request})

@app.get("/api/metrics")
async def get_metrics():
    """Get current metrics data."""
    return {
        "metrics": monitoring_data.metrics,
        "system": monitoring_data.get_system_stats(),
        "models": monitoring_data.get_model_info(),
        "data_stats": monitoring_data.get_data_stats(),
    }

@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    """WebSocket endpoint for real-time updates."""
    await manager.connect(websocket)
    try:
        while True:
            # Keep connection alive
            await asyncio.sleep(1)
    except WebSocketDisconnect:
        manager.disconnect(websocket)

# Background task for updates
async def update_metrics():
    """Periodically update metrics and broadcast to clients."""
    while True:
        try:
            # In a real implementation, you'd collect actual metrics here
            current_metrics = {
                'fps': np.random.normal(30, 2),
                'latency': np.random.normal(50, 5),
                'confidence': np.random.normal(0.85, 0.1),
                'memory_usage': np.random.normal(60, 5),
                'cpu_usage': np.random.normal(45, 10),
                'gpu_usage': np.random.normal(30, 5),
            }
            
            monitoring_data.update_metrics(current_metrics)
            
            # Prepare data for the dashboard
            metrics_df = monitoring_data.get_metrics_dataframe()
            
            # Create plots
            fig = make_subplots(rows=2, cols=2, subplot_titles=(
                "Frame Rate (FPS)", 
                "Processing Latency (ms)",
                "Model Confidence",
                "System Resource Usage"
            ))
            
            # Add traces for each metric
            if 'fps' in metrics_df:
                fig.add_trace(
                    go.Scatter(x=metrics_df['fps']['time'], y=metrics_df['fps']['value'], name="FPS"),
                    row=1, col=1
                )
                
            if 'latency' in metrics_df:
                fig.add_trace(
                    go.Scatter(x=metrics_df['latency']['time'], y=metrics_df['latency']['value'], name="Latency (ms)"),
                    row=1, col=2
                )
                
            if 'confidence' in metrics_df:
                fig.add_trace(
                    go.Scatter(x=metrics_df['confidence']['time'], y=metrics_df['confidence']['value'], name="Confidence"),
                    row=2, col=1
                )
            
            # System resources
            sys_stats = monitoring_data.get_system_stats()
            fig.add_trace(
                go.Bar(
                    x=['CPU', 'Memory', 'GPU'],
                    y=[sys_stats['cpu_usage'], sys_stats['memory_usage'], sys_stats['gpu_usage']],
                    name="Usage %"
                ),
                row=2, col=2
            )
            
            # Update layout
            fig.update_layout(
                height=800,
                showlegend=True,
                title_text="Stereo Vision System Metrics"
            )
            
            # Convert to JSON and broadcast
            plot_json = fig.to_json()
            await manager.broadcast(json.dumps({
                "type": "metrics_update",
                "data": json.loads(plot_json),
                "system": sys_stats,
                "models": monitoring_data.get_model_info(),
                "data_stats": monitoring_data.get_data_stats(),
            }))
            
        except Exception as e:
            print(f"Error updating metrics: {e}")
        
        await asyncio.sleep(CONFIG["update_interval"])

@app.on_event("startup")
async def startup_event():
    """Start background tasks when the application starts."""
    asyncio.create_task(update_metrics())

if __name__ == "__main__":
    # Create necessary directories
    os.makedirs("templates", exist_ok=True)
    os.makedirs("static/css", exist_ok=True)
    os.makedirs("static/js", exist_ok=True)
    
    # Create dashboard template if it doesn't exist
    if not os.path.exists("templates/dashboard.html"):
        with open("templates/dashboard.html", "w") as f:
            f.write("""
            <!DOCTYPE html>
            <html>
            <head>
                <title>Stereo Vision Monitoring</title>
                <script src="https://cdn.plot.ly/plotly-latest.min.js"></script>
                <link href="https://cdn.jsdelivr.net/npm/tailwindcss@2.2.19/dist/tailwind.min.css" rel="stylesheet">
                <script src="https://cdn.jsdelivr.net/npm/axios/dist/axios.min.js"></script>
            </head>
            <body class="bg-gray-100">
                <div class="container mx-auto px-4 py-8">
                    <h1 class="text-3xl font-bold mb-6">Stereo Vision System Dashboard</h1>
                    
                    <!-- Metrics Overview -->
                    <div class="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
                        <div class="bg-white p-6 rounded-lg shadow">
                            <h3 class="text-lg font-semibold mb-2">System Status</h3>
                            <div id="system-stats" class="space-y-2">
                                <!-- Filled by JavaScript -->
                            </div>
                        </div>
                        
                        <div class="bg-white p-6 rounded-lg shadow">
                            <h3 class="text-lg font-semibold mb-2">Data Collection</h3>
                            <div id="data-stats" class="space-y-2">
                                <!-- Filled by JavaScript -->
                            </div>
                        </div>
                        
                        <div class="bg-white p-6 rounded-lg shadow">
                            <h3 class="text-lg font-semibold mb-2">Model Information</h3>
                            <div id="model-info">
                                <!-- Filled by JavaScript -->
                            </div>
                        </div>
                    </div>
                    
                    <!-- Main Metrics Plot -->
                    <div class="bg-white p-6 rounded-lg shadow mb-8">
                        <div id="metrics-plot" style="height: 800px;"></div>
                    </div>
                    
                    <!-- Defect Statistics -->
                    <div class="bg-white p-6 rounded-lg shadow">
                        <h3 class="text-lg font-semibold mb-4">Defect Statistics</h3>
                        <div id="defect-stats" class="grid grid-cols-2 md:grid-cols-4 gap-4">
                            <!-- Filled by JavaScript -->
                        </div>
                    </div>
                </div>
                
                <script>
                    // WebSocket connection
                    let socket;
                    
                    function connectWebSocket() {
                        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
                        const wsUrl = `${protocol}//${window.location.host}/ws`;
                        socket = new WebSocket(wsUrl);
                        
                        socket.onmessage = (event) => {
                            const data = JSON.parse(event.data);
                            if (data.type === 'metrics_update') {
                                updateDashboard(data);
                            }
                        };
                        
                        socket.onclose = () => {
                            console.log('WebSocket disconnected. Reconnecting...');
                            setTimeout(connectWebSocket, 1000);
                        };
                        
                        socket.onerror = (error) => {
                            console.error('WebSocket error:', error);
                            socket.close();
                        };
                    }
                    
                    // Initialize dashboard with initial data
                    async function initDashboard() {
                        try {
                            const response = await fetch('/api/metrics');
                            const data = await response.json();
                            updateDashboard({
                                data: {
                                    data: [{
                                        type: 'scatter',
                                        x: [],
                                        y: []
                                    }]
                                },
                                system: data.system,
                                models: data.models,
                                data_stats: data.data_stats
                            });
                        } catch (error) {
                            console.error('Error initializing dashboard:', error);
                        }
                    }
                    
                    // Update the dashboard with new data
                    function updateDashboard(data) {
                        // Update metrics plot
                        if (data.data) {
                            Plotly.react('metrics-plot', data.data.data, data.data.layout || {});
                        }
                        
                        // Update system stats
                        if (data.system) {
                            const sysStats = document.getElementById('system-stats');
                            sysStats.innerHTML = `
                                <p>CPU: <span class="font-mono">${data.system.cpu_usage.toFixed(1)}%</span></p>
                                <p>Memory: <span class="font-mono">${data.system.memory_usage.toFixed(1)}%</span></p>
                                <p>GPU: <span class="font-mono">${data.system.gpu_usage.toFixed(1)}%</span></p>
                                <p>Disk: <span class="font-mono">${data.system.disk_usage.toFixed(1)}%</span></p>
                                <p>Uptime: <span class="font-mono">${data.system.uptime}</span></p>
                            `;
                        }
                        
                        // Update data stats
                        if (data.data_stats) {
                            const dataStats = document.getElementById('data-stats');
                            dataStats.innerHTML = `
                                <p>Total Samples: <span class="font-mono">${data.data_stats.total_samples}</span></p>
                                <p>Today's Samples: <span class="font-mono">${data.data_stats.samples_today}</span></p>
                                <p>Sample Rate: <span class="font-mono">${(data.data_stats.samples_today / 24).toFixed(1)}/hr</span></p>
                            `;
                            
                            // Update defect stats
                            if (data.data_stats.defect_stats) {
                                const defectStats = document.getElementById('defect-stats');
                                defectStats.innerHTML = Object.entries(data.data_stats.defect_stats)
                                    .map(([type, count]) => `
                                        <div class="bg-gray-100 p-4 rounded">
                                            <div class="text-sm text-gray-600">${type}</div>
                                            <div class="text-2xl font-bold">${count}</div>
                                        </div>
                                    `)
                                    .join('');
                            }
                        }
                        
                        // Update model info
                        if (data.models && data.models.length > 0) {
                            const latestModel = data.models[0];
                            const modelInfo = document.getElementById('model-info');
                            modelInfo.innerHTML = `
                                <p class="truncate">Model: <span class="font-mono">${latestModel.name}</span></p>
                                <p>Size: <span class="font-mono">${latestModel.size_mb.toFixed(2)} MB</span></p>
                                <p>Modified: <span class="font-mono">${new Date(latestModel.modified).toLocaleString()}</span></p>
                            `;
                        }
                    }
                    
                    // Initialize everything when the page loads
                    document.addEventListener('DOMContentLoaded', () => {
                        initDashboard();
                        connectWebSocket();
                    });
                </script>
            </body>
            </html>
            """)
    
    # Start the server
    uvicorn.run(app, host="0.0.0.0", port=CONFIG["port"])
