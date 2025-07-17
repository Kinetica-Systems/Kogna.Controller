#!/usr/bin/env python3
"""
Simple RS485 test script
Tests basic RS485 functionality without requiring the full application
"""

import socket
import json
import time
import sys

def test_rs485_simple():
    """Simple RS485 test"""
    
    print("🔧 Simple RS485 Test")
    print("=" * 30)
    
    # Try to connect to the IPC server
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)  # 5 second timeout
        sock.connect(('localhost', 5000))
        print("✅ Connected to IPC server")
        
        # Test a simple RS485 read command
        print("\n🔍 Testing RS485 read command...")
        
        request = {
            "Command": "rs485",
            "Args": ["1", "3001"]  # Read frequency from slave 1
        }
        
        message = json.dumps(request) + '\n'
        sock.send(message.encode('utf-8'))
        print(f"📤 Sent: rs485 1 3001")
        
        response = sock.recv(1024).decode('utf-8').strip()
        print(f"📥 Received: {response}")
        
        try:
            result = json.loads(response)
            print(f"✅ Command executed successfully")
            print(f"📊 Response: {result}")
        except json.JSONDecodeError:
            print(f"⚠️  Raw response: {response}")
            
        sock.close()
        
    except socket.timeout:
        print("❌ Connection timeout - server not responding")
    except ConnectionRefusedError:
        print("❌ Connection refused - server not running")
        print("\n💡 To start the server:")
        print("   1. Open the Kogna.Controller application")
        print("   2. Connect to your Kogna device")
        print("   3. The IPC server will start automatically")
    except Exception as e:
        print(f"❌ Connection failed: {e}")

def test_direct_connection():
    """Test if we can connect to the Kogna directly"""
    
    print("\n🔧 Testing Direct Kogna Connection")
    print("=" * 40)
    
    # Try common Kogna IP addresses
    kogna_ips = ['192.168.0.50', '192.168.1.50', '10.0.0.50', 'localhost']
    kogna_ports = [5000, 8080, 80]
    
    for ip in kogna_ips:
        for port in kogna_ports:
            try:
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.settimeout(2)
                sock.connect((ip, port))
                print(f"✅ Connected to {ip}:{port}")
                sock.close()
                return True
            except:
                pass
    
    print("❌ Could not connect to Kogna directly")
    return False

if __name__ == "__main__":
    test_rs485_simple()
    test_direct_connection()
    
    print("\n📋 Next Steps:")
    print("1. Start the Kogna.Controller application")
    print("2. Connect to your Kogna device")
    print("3. Run the test again")
    print("4. Or test directly on the Kogna using the terminal") 