#!/usr/bin/env python3
"""
Debug test script for RS485 passthrough
Tests persist data setting and C program execution
"""

import socket
import json
import time
import sys

def test_rs485_debug():
    """Debug RS485 functionality"""
    
    print("🔧 Debug RS485 Test")
    print("=" * 30)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: Set persist data manually
        print("\n🔍 Test 1: Set persist data manually")
        
        commands = [
            {"Command": "SetPersist UserData[0] 1", "Args": []},  # Slave address: 1
            {"Command": "SetPersist UserData[1] 12290", "Args": []},  # Register: 0x3002 (bus voltage)
            {"Command": "SetPersist UserData[2] 0", "Args": []},  # Value: 0 (read)
        ]
        
        for i, cmd in enumerate(commands):
            try:
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                print(f"📤 Sent: {cmd['Command']}")
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ Command failed: {e}")
        
        # Test 2: Execute C program
        print("\n🔍 Test 2: Execute C program")
        
        try:
            cmd = {"Command": "ExecThread 3", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: ExecThread 3")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ ExecThread failed: {e}")
        
        # Test 3: Wait and read result
        print("\n🔍 Test 3: Read result from persist data")
        
        time.sleep(2)  # Wait for C program to execute
        
        try:
            cmd = {"Command": "GetPersist UserData[10]", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: GetPersist UserData[10]")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if result.get('Status') == 'OK':
                    value = result.get('Result', '0')
                    print(f"📊 Result value: {value}")
                    if value != '0':
                        print(f"✅ Success! Register value: {value}")
                    else:
                        print(f"⚠️  No response (value=0)")
                else:
                    print(f"❌ Error: {result.get('Error', 'Unknown error')}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ GetPersist failed: {e}")
        
        # Test 4: Try the rs485 command directly
        print("\n🔍 Test 4: Try rs485 command directly")
        
        try:
            cmd = {"Command": "rs485", "Args": ["1", "3002"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: rs485 1 3002")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                print(f"📊 Response: {result}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ rs485 command failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_rs485_debug() 