#!/usr/bin/env python3
"""
Test C program execution
Verify if the C program is actually being executed on the Kogna
"""

import socket
import json
import time
import sys

def test_c_program_execution():
    """Test C program execution"""
    
    print("🔧 Test C Program Execution")
    print("=" * 35)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: Check if thread 3 is loaded
        print("\n🔍 Test 1: Check if thread 3 is loaded")
        
        try:
            cmd = {"Command": "execthread", "Args": ["3"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: execthread 3")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ execthread failed: {e}")
        
        # Test 2: Try to get console output
        print("\n🔍 Test 2: Try to get console output")
        
        try:
            cmd = {"Command": "serviceconsole", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: serviceconsole")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ serviceconsole failed: {e}")
        
        # Test 3: Try a simple command to see if Kogna is responding
        print("\n🔍 Test 3: Simple Kogna command")
        
        try:
            cmd = {"Command": "version", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: version")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ version command failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_c_program_execution() 