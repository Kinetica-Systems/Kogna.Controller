#!/usr/bin/env python3
"""
Test Kogna connection and basic commands
"""

import socket
import json
import time
import sys

def test_kogna_connection():
    """Test Kogna connection"""
    
    print("🔧 Test Kogna Connection")
    print("=" * 30)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: Simple command
        print("\n🔍 Test 1: Simple command")
        
        try:
            cmd = {"Command": "version", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: version")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ version command failed: {e}")
        
        # Test 2: SetPersist with simple value
        print("\n🔍 Test 2: SetPersist with simple value")
        
        try:
            cmd = {"Command": "setpersist", "Args": ["UserData[0]", "42"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: setpersist UserData[0] 42")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ setpersist failed: {e}")
        
        # Test 3: GetPersist immediately after set
        print("\n🔍 Test 3: GetPersist immediately after set")
        
        try:
            cmd = {"Command": "getpersist", "Args": ["UserData[0]"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: getpersist UserData[0]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ getpersist failed: {e}")
        
        # Test 4: Check if Kogna is responding to basic commands
        print("\n🔍 Test 4: Check Kogna response")
        
        try:
            cmd = {"Command": "status", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: status")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ status command failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_kogna_connection() 