import os
import time
import json
import cv2
import numpy as np
from datetime import datetime
from pathlib import Path
import argparse
from typing import Tuple, Dict, Optional

class StereoDataCollector:
    """Tool for collecting stereo image pairs and annotations for bead analysis."""
    
    def __init__(self, output_dir: str, camera_left: int = 0, camera_right: int = 1):
        """
        Initialize the data collector.
        
        Args:
            output_dir: Base directory to save collected data
            camera_left: Index of left camera
            camera_right: Index of right camera
        ""
        self.output_dir = Path(output_dir)
        self.camera_left = cv2.VideoCapture(camera_left)
        self.camera_right = cv2.VideoCapture(camera_right)
        
        # Set camera properties (adjust based on your cameras)
        self._setup_camera(self.camera_left)
        self._setup_camera(self.camera_right)
        
        # Create output directories
        self.output_dir.mkdir(parents=True, exist_ok=True)
        (self.output_dir / 'left').mkdir(exist_ok=True)
        (self.output_dir / 'right').mkdir(exist_ok=True)
        (self.output_dir / 'labels').mkdir(exist_ok=True)
        
        # Annotation data
        self.current_annotation = {
            'width_mm': 0.0,      # Bead width in mm
            'height_mm': 0.0,     # Bead height in mm
            'cross_section': [],   # Cross-sectional profile
            'defects': [],         # List of defects
            'timestamp': "",       # ISO format timestamp
            'camera_params': {     # Camera parameters
                'left': {'focal_length': 0, 'baseline_mm': 60.0},
                'right': {'focal_length': 0, 'baseline_mm': 60.0}
            }
        }
    
    def _setup_camera(self, cap):
        """Configure camera settings."""
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
        cap.set(cv2.CAP_PROP_FPS, 30)
        cap.set(cv2.CAP_PROP_AUTOFOCUS, 0)  # Turn off autofocus
        cap.set(cv2.CAP_PROP_FOCUS, 0)      # Manual focus
    
    def capture_stereo_pair(self) -> Tuple[np.ndarray, np.ndarray]:
        """Capture synchronized stereo image pair."""
        # Read frames from both cameras
        ret_left, frame_left = self.camera_left.read()
        ret_right, frame_right = self.camera_right.read()
        
        if not ret_left or not ret_right:
            raise RuntimeError("Failed to capture frames from one or both cameras")
            
        return frame_left, frame_right
    
    def save_sample(self, frame_left: np.ndarray, frame_right: np.ndarray, 
                   annotation: Optional[Dict] = None) -> str:
        """
        Save a stereo image pair and its annotation.
        
        Returns:
            str: Base filename of the saved sample
        """
        timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S_%f")
        base_name = f"bead_{timestamp}"
        
        # Save images
        cv2.imwrite(str(self.output_dir / 'left' / f"{base_name}.png"), frame_left)
        cv2.imwrite(str(self.output_dir / 'right' / f"{base_name}.png"), frame_right)
        
        # Save annotation
        annotation = annotation or self.current_annotation
        annotation['timestamp'] = datetime.utcnow().isoformat()
        
        with open(self.output_dir / 'labels' / f"{base_name}.json", 'w') as f:
            json.dump(annotation, f, indent=2)
        
        return base_name
    
    def interactive_capture(self):
        """Run interactive capture session."""
        print("Starting interactive capture session. Press 'c' to capture, 'q' to quit.")
        
        cv2.namedWindow("Left Camera", cv2.WINDOW_NORMAL)
        cv2.namedWindow("Right Camera", cv2.WINDOW_NORMAL)
        
        try:
            while True:
                # Capture frames
                frame_left, frame_right = self.capture_stereo_pair()
                
                # Display frames
                cv2.imshow("Left Camera", frame_left)
                cv2.imshow("Right Camera", frame_right)
                
                # Handle keypress
                key = cv2.waitKey(1) & 0xFF
                
                if key == ord('c'):  # Capture
                    try:
                        # Get annotation from user
                        self._get_annotation_from_user()
                        
                        # Save the sample
                        sample_id = self.save_sample(frame_left, frame_right)
                        print(f"Captured sample: {sample_id}")
                        
                        # Show success message
                        cv2.putText(frame_left, "Captured!", (50, 50), 
                                  cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                        cv2.imshow("Left Camera", frame_left)
                        cv2.waitKey(500)  # Show message for 500ms
                        
                    except Exception as e:
                        print(f"Error capturing sample: {e}")
                        
                elif key == ord('q'):  # Quit
                    break
                    
        finally:
            self.cleanup()
    
    def _get_annotation_from_user(self):
        """Get annotation data from user input."""
        print("\n--- Annotation ---")
        
        # Get bead dimensions
        try:
            self.current_annotation['width_mm'] = float(input("Enter bead width (mm): "))
            self.current_annotation['height_mm'] = float(input("Enter bead height (mm): "))
            
            # For now, we'll just store empty lists - these would be filled with actual
            # measurements from calipers or other sensors in a real setup
            self.current_annotation['cross_section'] = []
            self.current_annotation['defects'] = []
            
        except ValueError:
            print("Invalid input. Using default values.")
    
    def cleanup(self):
        """Release resources."""
        self.camera_left.release()
        self.camera_right.release()
        cv2.destroyAllWindows()

def parse_args():
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(description='Stereo Data Collection Tool')
    parser.add_argument('--output-dir', type=str, default='data/bead_dataset',
                       help='Directory to save collected data')
    parser.add_argument('--left-cam', type=int, default=0,
                       help='Index of left camera')
    parser.add_argument('--right-cam', type=int, default=1,
                       help='Index of right camera')
    return parser.parse_args()

if __name__ == "__main__":
    args = parse_args()
    
    print(f"Starting data collection in {args.output_dir}")
    print(f"Cameras: Left={args.left_cam}, Right={args.right_cam}")
    
    collector = StereoDataCollector(
        output_dir=args.output_dir,
        camera_left=args.left_cam,
        camera_right=args.right_cam
    )
    
    try:
        collector.interactive_capture()
    except KeyboardInterrupt:
        print("\nData collection stopped by user.")
    finally:
        collector.cleanup()
    
    print(f"Data collection complete. Data saved to {args.output_dir}")
