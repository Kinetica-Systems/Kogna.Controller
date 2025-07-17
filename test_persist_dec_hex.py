#!/usr/bin/env python3
"""
Test GetPersistDec and GetPersistHex commands
"""

import socket
import json
import time
import sys

def test_persist_dec_hex():
    """Test GetPersistDec and GetPersistHex commands"""
    
    print("🔧 Test GetPersistDec and GetPersistHex")
    print("=" * 40)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: Set a known value first
        print("\n🔍 Test 1: Set a known value")
        
        try:
            cmd = {"Command": "setpersist", "Args": ["UserData[10]", "3253"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: setpersist UserData[10] 3253")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ setpersist failed: {e}")
        
        # Test 2: Try GetPersistDec
        print("\n🔍 Test 2: Try GetPersistDec")
        
        try:
            cmd = {"Command": "getpersistdec", "Args": ["UserData[10]"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: getpersistdec UserData[10]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ getpersistdec failed: {e}")
        
        # Test 3: Try GetPersistHex
        print("\n🔍 Test 3: Try GetPersistHex")
        
        try:
            cmd = {"Command": "getpersisthex", "Args": ["UserData[10]"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: getpersisthex UserData[10]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ getpersisthex failed: {e}")
        
        # Test 4: Try regular getpersist for comparison
        print("\n🔍 Test 4: Try regular getpersist for comparison")
        
        try:
            cmd = {"Command": "getpersist", "Args": ["UserData[10]"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: getpersist UserData[10]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ getpersist failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_persist_dec_hex() 