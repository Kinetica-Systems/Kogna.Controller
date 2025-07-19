#!/usr/bin/env python3
"""
Data processing pipeline for auto-collected deposition images.

This script processes images collected during the deposition process,
applies quality filters, and prepares them for model training.
"""

import os
import json
import shutil
import random
from pathlib import Path
from typing import Dict, List, Tuple, Optional
import numpy as np
from PIL import Image, ImageOps
import cv2
from tqdm import tqdm

class AutoDataProcessor:
    """Processes auto-collected data for training."""
    
    def __init__(self, 
                 input_dir: str = 'data/auto_collected',
                 output_dir: str = 'data/processed',
                 min_confidence: float = 0.7,
                 min_pixels: int = 1000,
                 train_split: float = 0.8):
        """
        Initialize the data processor.
        
        Args:
            input_dir: Directory with auto-collected data
            output_dir: Directory to save processed data
            min_confidence: Minimum prediction confidence to include sample
            min_pixels: Minimum number of non-zero pixels in mask
            train_split: Fraction of data to use for training (rest for validation)
        """
        self.input_dir = Path(input_dir)
        self.output_dir = Path(output_dir)
        self.min_confidence = min_confidence
        self.min_pixels = min_pixels
        self.train_split = train_split
        
        # Create output directories
        (self.output_dir / 'train/left').mkdir(parents=True, exist_ok=True)
        (self.output_dir / 'train/right').mkdir(exist_ok=True)
        (self.output_dir / 'train/labels').mkdir(exist_ok=True)
        (self.output_dir / 'val/left').mkdir(exist_ok=True)
        (self.output_dir / 'val/right').mkdir(exist_ok=True)
        (self.output_dir / 'val/labels').mkdir(exist_ok=True)
    
    def process_dataset(self):
        """Process all auto-collected data."""
        print(f"Processing auto-collected data from {self.input_dir}")
        
        # Find all samples (pairs of left/right images and annotations)
        samples = self._find_samples()
        print(f"Found {len(samples)} samples")
        
        # Process each sample
        processed_samples = []
        for sample in tqdm(samples, desc="Processing samples"):
            try:
                result = self._process_sample(sample)
                if result is not None:
                    processed_samples.append(result)
            except Exception as e:
                print(f"Error processing {sample['left']}: {e}")
        
        print(f"Processed {len(processed_samples)}/{len(samples)} samples")
        
        # Split into train/val
        random.shuffle(processed_samples)
        split_idx = int(len(processed_samples) * self.train_split)
        train_samples = processed_samples[:split_idx]
        val_samples = processed_samples[split_idx:]
        
        # Save processed data
        self._save_dataset(train_samples, 'train')
        self._save_dataset(val_samples, 'val')
        
        print(f"Saved {len(train_samples)} training and {len(val_samples)} validation samples")
        return len(train_samples), len(val_samples)
    
    def _find_samples(self) -> List[Dict]:
        """Find all valid samples in the input directory."""
        samples = []
        
        # Look for samples in both train and val splits
        for split in ['train', 'val']:
            left_dir = self.input_dir / split / 'left'
            if not left_dir.exists():
                continue
                
            for img_file in left_dir.glob('*.png'):
                base_name = img_file.stem
                right_img = self.input_dir / split / 'right' / img_file.name
                label_file = self.input_dir / split / 'labels' / f"{base_name}.json"
                
                if right_img.exists() and label_file.exists():
                    samples.append({
                        'left': img_file,
                        'right': right_img,
                        'label': label_file,
                        'split': split
                    })
        
        return samples
    
    def _process_sample(self, sample: Dict) -> Optional[Dict]:
        """Process a single sample."""
        # Load annotation
        with open(sample['label'], 'r') as f:
            annotation = json.load(f)
        
        # Skip low-confidence predictions
        if annotation.get('prediction_confidence', 0) < self.min_confidence:
            return None
        
        # Load and validate images
        left_img = Image.open(sample['left']).convert('RGB')
        right_img = Image.open(sample['right']).convert('RGB')
        
        # Skip if images are too dark or invalid
        if self._is_low_quality(left_img) or self._is_low_quality(right_img):
            return None
        
        # Create output annotation
        output_annotation = {
            'width': float(annotation.get('width', 0)),
            'height': float(annotation.get('height', 0)),
            'cross_section': [float(x) for x in annotation.get('cross_section', [])],
            'defects': annotation.get('defects', []),
            'process_parameters': annotation.get('process_parameters', {})
        }
        
        return {
            'left': left_img,
            'right': right_img,
            'annotation': output_annotation,
            'split': sample['split']
        }
    
    def _is_low_quality(self, img: Image.Image) -> bool:
        """Check if image is low quality (too dark, blurry, etc.)."""
        # Convert to grayscale
        gray = np.array(ImageOps.grayscale(img))
        
        # Check if image is too dark
        if np.mean(gray) < 10:  # Very dark
            return True
            
        # Check if image is too blurry (using variance of Laplacian)
        laplacian = cv2.Laplacian(gray, cv2.CV_64F)
        if laplacian.var() < 50:  # Threshold for blur detection
            return True
            
        return False
    
    def _save_dataset(self, samples: List[Dict], split: str):
        """Save processed samples to the output directory."""
        output_dir = self.output_dir / split
        
        for i, sample in enumerate(samples):
            base_name = f"{split}_{i:06d}"
            
            # Save images
            sample['left'].save(output_dir / 'left' / f"{base_name}.png")
            sample['right'].save(output_dir / 'right' / f"{base_name}.png")
            
            # Save annotation
            with open(output_dir / 'labels' / f"{base_name}.json", 'w') as f:
                json.dump(sample['annotation'], f, indent=2)


def main():
    import argparse
    
    parser = argparse.ArgumentParser(description='Process auto-collected deposition data')
    parser.add_argument('--input-dir', default='data/auto_collected',
                       help='Directory with auto-collected data')
    parser.add_argument('--output-dir', default='data/processed',
                       help='Directory to save processed data')
    parser.add_argument('--min-confidence', type=float, default=0.7,
                       help='Minimum prediction confidence to include sample')
    parser.add_argument('--min-pixels', type=int, default=1000,
                       help='Minimum number of non-zero pixels in mask')
    parser.add_argument('--train-split', type=float, default=0.8,
                       help='Fraction of data to use for training')
    
    args = parser.parse_args()
    
    processor = AutoDataProcessor(
        input_dir=args.input_dir,
        output_dir=args.output_dir,
        min_confidence=args.min_confidence,
        min_pixels=args.min_pixels,
        train_split=args.train_split
    )
    
    train_count, val_count = processor.process_dataset()
    print(f"Processing complete: {train_count} training, {val_count} validation samples")


if __name__ == "__main__":
    main()
