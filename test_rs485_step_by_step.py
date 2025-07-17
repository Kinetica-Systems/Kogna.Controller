#!/usr/bin/env python3
"""
Step-by-step RS485 test
Debug persist data and C program execution
"""

import socket
import json
import time
import sys

def test_rs485_step_by_step():
    """Step-by-step RS485 test"""
    
    print("🔧 Step-by-Step RS485 Test")
    print("=" * 40)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Step 1: Check current persist data
        print("\n🔍 Step 1: Check current persist data")
        
        for i in range(5):
            try:
                cmd = {"Command": f"GetPersist UserData[{i}]", "Args": []}
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                print(f"📤 Sent: GetPersist UserData[{i}]")
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ GetPersist failed: {e}")
        
        # Step 2: Set persist data one by one
        print("\n🔍 Step 2: Set persist data one by one")
        
        persist_data = [
            (0, 1),      # Slave address
            (1, 12290),  # Register 0x3002 (bus voltage)
            (2, 0),      # Value (read)
        ]
        
        for index, value in persist_data:
            try:
                cmd = {"Command": f"SetPersist UserData[{index}] {value}", "Args": []}
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                print(f"📤 Sent: SetPersist UserData[{index}] {value}")
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ SetPersist failed: {e}")
        
        # Step 3: Verify persist data was set
        print("\n🔍 Step 3: Verify persist data was set")
        
        for i in range(5):
            try:
                cmd = {"Command": f"GetPersist UserData[{i}]", "Args": []}
                message = json.dumps(cmd) + '\n'
                sock.send(message.encode('utf-8'))
                print(f"📤 Sent: GetPersist UserData[{i}]")
                
                response = sock.recv(1024).decode('utf-8').strip()
                print(f"📥 Received: {response}")
                
                time.sleep(0.1)
                
            except Exception as e:
                print(f"❌ GetPersist failed: {e}")
        
        # Step 4: Execute C program
        print("\n🔍 Step 4: Execute C program")
        
        try:
            cmd = {"Command": "ExecThread 3", "Args": []}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: ExecThread 3")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
        except Exception as e:
            print(f"❌ ExecThread failed: {e}")
        
        # Step 5: Wait and check result
        print("\n🔍 Step 5: Wait and check result")
        
        time.sleep(3)  # Wait for C program to execute
        
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
                    if value != '0' and value != 'EXECTHREAD':
                        print(f"✅ Success! Register value: {value}")
                    else:
                        print(f"⚠️  No valid response (value={value})")
                else:
                    print(f"❌ Error: {result.get('Error', 'Unknown error')}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ GetPersist failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_rs485_step_by_step() 