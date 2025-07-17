#!/usr/bin/env python3
"""
Test SetPersistDec command functionality
Tests the new SetPersistDec command that was added to the IPC server
"""

import socket
import json
import time
import sys

def test_setpersistdec():
    """Test SetPersistDec command functionality"""
    
    print("🔧 Test SetPersistDec Command")
    print("=" * 35)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: Set persist data using SetPersistDec
        print("\n🔍 Test 1: Set persist data using SetPersistDec")
        
        persist_data = [
            ("0", "1"),      # Slave address
            ("1", "12290"),  # Register 0x3002 (bus voltage)
            ("2", "0"),      # Value (read)
        ]
        
        for index, value in persist_data:
            try:
                cmd = {"Command": "setpersistdec", "Args": [index, value]}
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                print(f"📤 Sent: setpersistdec {index} {value}")
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ setpersistdec failed: {e}")
        
        # Test 2: Verify persist data using GetPersistDec
        print("\n🔍 Test 2: Verify persist data using GetPersistDec")
        
        for i in range(5):
            try:
                cmd = {"Command": "getpersistdec", "Args": [str(i)]}
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                print(f"📤 Sent: getpersistdec {i}")
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ getpersistdec failed: {e}")
        
        # Test 3: Execute C program
        print("\n🔍 Test 3: Execute C program")
        
        try:
            cmd = {"Command": "execthread", "Args": ["3"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: execthread 3")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ execthread failed: {e}")
        
        # Test 4: Wait and check result using GetPersistDec
        print("\n🔍 Test 4: Wait and check result using GetPersistDec")
        
        time.sleep(3)
        
        try:
            cmd = {"Command": "getpersistdec", "Args": ["10"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: getpersistdec 10")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if result.get('Status') == 'OK':
                    value = result.get('Result', '0')
                    print(f"📊 Result value: {value}")
                    if value != '0' and value != 'EXECTHREAD':
                        print(f"✅ Success! Register value: {value}")
                    else:
                        print(f"⚠️  No valid response (value={value})")
                else:
                    print(f"❌ Error: {result.get('Error', 'Unknown error')}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ getpersistdec failed: {e}")
        
        # Test 5: Compare SetPersist vs SetPersistDec
        print("\n🔍 Test 5: Compare SetPersist vs SetPersistDec")
        
        # Set using regular SetPersist
        try:
            cmd = {"Command": "setpersist", "Args": ["UserData[15]", "12345"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: setpersist UserData[15] 12345")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ setpersist failed: {e}")
        
        # Set using SetPersistDec
        try:
            cmd = {"Command": "setpersistdec", "Args": ["16", "12345"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: setpersistdec 16 12345")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ setpersistdec failed: {e}")
        
        # Read both back
        for i in [15, 16]:
            try:
                if i == 15:
                    cmd = {"Command": "getpersist", "Args": [f"UserData[{i}]"]}
                    print(f"📤 Sent: getpersist UserData[{i}]")
                else:
                    cmd = {"Command": "getpersistdec", "Args": [str(i)]}
                    print(f"📤 Sent: getpersistdec {i}")
                
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ getpersist/getpersistdec failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_setpersistdec() 