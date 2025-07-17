#!/usr/bin/env python3
"""
Simple connection test for Kogna.Controller
"""

import socket
import time

def test_ports():
    """Test common ports to see if the application is listening"""
    ports = [5000, 8080, 3000, 4000, 6000, 7000, 8000, 9000]
    
    for port in ports:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(1)
            result = sock.connect_ex(('localhost', port))
            if result == 0:
                print(f"✓ Port {port} is open and listening")
                sock.close()
                return port
            else:
                print(f"✗ Port {port} is not listening")
            sock.close()
        except Exception as e:
            print(f"✗ Error testing port {port}: {e}")
    
    return None

def test_connection(port):
    """Test connection to the found port"""
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        print(f"Attempting to connect to localhost:{port}...")
        sock.connect(('localhost', port))
        print("✓ Connection successful!")
        
        # Send a simple test command
        test_cmd = "version\n"
        print(f"Sending test command: {test_cmd.strip()}")
        sock.send(test_cmd.encode())
        
        # Try to receive response
        try:
            response = sock.recv(1024).decode().strip()
            print(f"Response: {response}")
        except:
            print("No response received")
        
        sock.close()
        return True
        
    except Exception as e:
        print(f"✗ Connection failed: {e}")
        return False

if __name__ == "__main__":
    print("Testing Kogna.Controller connection...")
    print("="*40)
    
    # Test for listening ports
    port = test_ports()
    
    if port:
        print(f"\nFound listening port: {port}")
        test_connection(port)
    else:
        print("\nNo listening ports found.")
        print("The application may not be running or may be using a different port.")
        print("\nTo start the application:")
        print("1. Open a new terminal")
        print("2. Navigate to the App directory")
        print("3. Run: dotnet run") 