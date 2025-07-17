#!/usr/bin/env python3
"""
Simple IPC connection test
Tests if the IPC server is running and accessible
"""

import socket
import json
import time

def test_ipc_connection():
    """Test if IPC server is accessible"""
    
    print("🔧 Testing IPC Server Connection")
    print("=" * 35)
    
    # Try to connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)  # 5 second timeout
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test a simple command
        try:
            cmd = {"Command": "version", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: version")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ Command failed: {e}")
        
        sock.close()
        
    except socket.timeout:
        print("❌ Connection timeout - server not responding")
        print("\n💡 The Kogna.Controller application needs to be running")
        print("   and connected to a Kogna device for the IPC server to start.")
    except ConnectionRefusedError:
        print("❌ Connection refused - IPC server not running")
        print("\n💡 To start the IPC server:")
        print("   1. Open the Kogna.Controller application")
        print("   2. Connect to your Kogna device")
        print("   3. The IPC server will start automatically on port 5000")
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_ipc_connection() 