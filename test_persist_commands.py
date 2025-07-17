#!/usr/bin/env python3
"""
Test different persist command formats
Try GetPersistDec, GetPersistHex, and other variations
"""

import socket
import json
import time
import sys

def test_persist_commands():
    """Test different persist command formats"""
    
    print("🔧 Test Persist Commands")
    print("=" * 30)
    
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
        
        # Test 4: Try raw GetPersist command
        print("\n🔍 Test 4: Try raw GetPersist command")
        
        try:
            cmd = {"Command": "GetPersist", "Args": ["UserData[10]"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: GetPersist UserData[10]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ GetPersist failed: {e}")
        
        # Test 5: Try lowercase getpersist
        print("\n🔍 Test 5: Try lowercase getpersist")
        
        try:
            cmd = {"Command": "getpersist", "Args": ["UserData[10]"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: getpersist UserData[10]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ getpersist failed: {e}")
        
        # Test 6: Try sending raw command to Kogna
        print("\n🔍 Test 6: Try raw command to Kogna")
        
        try:
            cmd = {"Command": "GetPersist UserData[10]", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: GetPersist UserData[10] (raw)")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ raw command failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_persist_commands() 