#!/usr/bin/env python3
"""
Test script for Kogna.Controller hardware control features
Tests PWM control for lasers and step/dir control for wire feeder
"""

import socket
import json
import time
import sys

class KognaHardwareTester:
    def __init__(self, host='localhost', port=5000):
        self.host = host
        self.port = port
        self.socket = None
        
    def connect(self):
        """Connect to the Kogna controller"""
        try:
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.socket.connect((self.host, self.port))
            print(f"Connected to {self.host}:{self.port}")
            return True
        except Exception as e:
            print(f"Connection failed: {e}")
            return False
    
    def send_command(self, command):
        """Send a command to the Kogna controller"""
        if not self.socket:
            print("Not connected")
            return None
            
        try:
            # Create IPC request
            request = {
                "Command": command,
                "Args": []
            }
            
            # Send request
            self.socket.send((json.dumps(request) + '\n').encode())
            
            # Receive response
            response = self.socket.recv(1024).decode().strip()
            return json.loads(response)
            
        except Exception as e:
            print(f"Command failed: {e}")
            return None
    
    def test_laser_control(self):
        """Test laser control commands"""
        print("\n=== Testing Laser Control ===")
        
        # Test laser 1
        print("Testing Laser 1...")
        response = self.send_command("laser 1 on")
        print(f"Laser 1 on: {response}")
        time.sleep(1)
        
        response = self.send_command("laser 1 128")
        print(f"Laser 1 50%: {response}")
        time.sleep(1)
        
        response = self.send_command("laser 1 off")
        print(f"Laser 1 off: {response}")
        time.sleep(1)
        
        # Test laser 2
        print("Testing Laser 2...")
        response = self.send_command("laser 2 on")
        print(f"Laser 2 on: {response}")
        time.sleep(1)
        
        response = self.send_command("laser 2 64")
        print(f"Laser 2 25%: {response}")
        time.sleep(1)
        
        response = self.send_command("laser 2 off")
        print(f"Laser 2 off: {response}")
        time.sleep(1)
    
    def test_wire_feeder_control(self):
        """Test wire feeder control commands"""
        print("\n=== Testing Wire Feeder Control ===")
        
        # Test step signal
        print("Testing step signal...")
        response = self.send_command("wirefeeder step high")
        print(f"Step high: {response}")
        time.sleep(0.5)
        
        response = self.send_command("wirefeeder step low")
        print(f"Step low: {response}")
        time.sleep(0.5)
        
        # Test direction signal
        print("Testing direction signal...")
        response = self.send_command("wirefeeder dir high")
        print(f"Dir high: {response}")
        time.sleep(0.5)
        
        response = self.send_command("wirefeeder dir low")
        print(f"Dir low: {response}")
        time.sleep(0.5)
    
    def test_direct_pwm(self):
        """Test direct PWM control"""
        print("\n=== Testing Direct PWM Control ===")
        
        # Test channel 8 (Laser 1)
        print("Testing PWM channel 8...")
        response = self.send_command("pwm 8 255")
        print(f"PWM 8 255: {response}")
        time.sleep(1)
        
        response = self.send_command("pwm 8 128")
        print(f"PWM 8 128: {response}")
        time.sleep(1)
        
        response = self.send_command("pwm 8 0")
        print(f"PWM 8 0: {response}")
        time.sleep(1)
        
        # Test channel 9 (Laser 2)
        print("Testing PWM channel 9...")
        response = self.send_command("pwm 9 255")
        print(f"PWM 9 255: {response}")
        time.sleep(1)
        
        response = self.send_command("pwm 9 64")
        print(f"PWM 9 64: {response}")
        time.sleep(1)
        
        response = self.send_command("pwm 9 0")
        print(f"PWM 9 0: {response}")
        time.sleep(1)
    
    def test_gcode_m42(self):
        """Test G-code M42 commands"""
        print("\n=== Testing G-code M42 Commands ===")
        
        # Test M42 commands via gcode command
        print("Testing M42 P8 S128...")
        response = self.send_command("gcode M42 P8 S128")
        print(f"M42 P8 S128: {response}")
        time.sleep(1)
        
        print("Testing M42 P9 S255...")
        response = self.send_command("gcode M42 P9 S255")
        print(f"M42 P9 S255: {response}")
        time.sleep(1)
        
        print("Testing M42 P10 S1...")
        response = self.send_command("gcode M42 P10 S1")
        print(f"M42 P10 S1: {response}")
        time.sleep(0.5)
        
        print("Testing M42 P10 S0...")
        response = self.send_command("gcode M42 P10 S0")
        print(f"M42 P10 S0: {response}")
        time.sleep(0.5)
    
    def run_all_tests(self):
        """Run all hardware control tests"""
        print("Starting Kogna Hardware Control Tests")
        print("=" * 50)
        
        if not self.connect():
            print("Failed to connect. Make sure the Kogna controller is running.")
            return
        
        try:
            # Run all tests
            self.test_laser_control()
            self.test_wire_feeder_control()
            self.test_direct_pwm()
            self.test_gcode_m42()
            
            print("\n" + "=" * 50)
            print("All tests completed!")
            
        except KeyboardInterrupt:
            print("\nTests interrupted by user")
        except Exception as e:
            print(f"Test error: {e}")
        finally:
            if self.socket:
                self.socket.close()
                print("Connection closed")

def main():
    """Main function"""
    if len(sys.argv) > 1:
        host = sys.argv[1]
    else:
        host = 'localhost'
    
    if len(sys.argv) > 2:
        port = int(sys.argv[2])
    else:
        port = 5000
    
    tester = KognaHardwareTester(host, port)
    tester.run_all_tests()

if __name__ == "__main__":
    main() 