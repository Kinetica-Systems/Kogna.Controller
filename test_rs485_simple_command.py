#!/usr/bin/env python3
"""
Simple RS485 command test
Tests the rs485 command directly without manual persist data setting
"""

import socket
import json
import time
import sys

def test_rs485_simple_command():
    """Test RS485 command directly"""
    
    print("🔧 Simple RS485 Command Test")
    print("=" * 35)
    
    # Connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(10)
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test 1: RS485 read command for bus voltage (0x3002)
        print("\n🔍 Test 1: RS485 read bus voltage (0x3002)")
        
        try:
            cmd = {"Command": "rs485", "Args": ["1", "3002"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: rs485 1 3002")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if result.get('Status') == 'OK':
                    value = result.get('Result', '0')
                    print(f"📊 Response: {value}")
                    if value != '0' and 'No response' not in value:
                        print(f"✅ Success! Result: {value}")
                    else:
                        print(f"⚠️  No response or error: {value}")
                else:
                    print(f"❌ Error: {result.get('Error', 'Unknown error')}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ rs485 command failed: {e}")
        
        # Test 2: RS485 read command for frequency (0x3001)
        print("\n🔍 Test 2: RS485 read frequency (0x3001)")
        
        try:
            cmd = {"Command": "rs485", "Args": ["1", "3001"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: rs485 1 3001")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if result.get('Status') == 'OK':
                    value = result.get('Result', '0')
                    print(f"📊 Response: {value}")
                    if value != '0' and 'No response' not in value:
                        print(f"✅ Success! Result: {value}")
                    else:
                        print(f"⚠️  No response or error: {value}")
                else:
                    print(f"❌ Error: {result.get('Error', 'Unknown error')}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ rs485 command failed: {e}")
        
        # Test 3: Servo status command for voltage
        print("\n🔍 Test 3: Servo status command for voltage")
        
        try:
            cmd = {"Command": "servostatus", "Args": ["1", "voltage"]}
            message = json.dumps(cmd) + '\n'
            sock.send(message.encode('utf-8'))
            print(f"📤 Sent: servostatus 1 voltage")
            
            response = sock.recv(1024).decode('utf-8').strip()
            print(f"📥 Received: {response}")
            
            try:
                result = json.loads(response)
                if result.get('Status') == 'OK':
                    value = result.get('Result', '0')
                    print(f"📊 Response: {value}")
                    if value != '0' and 'No response' not in value:
                        print(f"✅ Success! Result: {value}")
                    else:
                        print(f"⚠️  No response or error: {value}")
                else:
                    print(f"❌ Error: {result.get('Error', 'Unknown error')}")
            except json.JSONDecodeError:
                print(f"⚠️  Raw response: {response}")
                
        except Exception as e:
            print(f"❌ servostatus command failed: {e}")
        
        sock.close()
        
    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    test_rs485_simple_command() 