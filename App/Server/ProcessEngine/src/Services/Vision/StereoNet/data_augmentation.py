"""
Data augmentation pipeline for stereo image pairs and their annotations.
Includes photometric and geometric transformations that preserve stereo correspondence.
"""

import random
import numpy as np
import cv2
from typing import Tuple, Dict, Any, List
import torch
from torchvision import transforms
import torchvision.transforms.functional as F

class StereoAugmentation:
    """Data augmentation for stereo image pairs with corresponding annotations."""
    
    def __init__(self, 
                 image_size: Tuple[int, int] = (256, 512),
                 use_color_jitter: bool = True,
                 use_geometric: bool = True,
                 use_cutout: bool = True,
                 use_mixup: bool = True):
        """
        Initialize the augmentation pipeline.
        
        Args:
            image_size: Target size for resizing (height, width)
            use_color_jitter: Whether to apply color jitter
            use_geometric: Whether to apply geometric transforms
            use_cutout: Whether to apply cutout augmentation
            use_mixup: Whether to apply mixup between samples
        """
        self.image_size = image_size
        self.use_color_jitter = use_color_jitter
        self.use_geometric = use_geometric
        self.use_cutout = use_cutout
        self.use_mixup = use_mixup
        
        # Photometric transforms (applied independently to left/right)
        self.photometric = transforms.Compose([
            transforms.ColorJitter(
                brightness=0.2,
                contrast=0.2,
                saturation=0.2,
                hue=0.05
            ) if use_color_jitter else transforms.Lambda(lambda x: x),
            transforms.GaussianBlur(kernel_size=3, sigma=(0.1, 2.0)),
            transforms.RandomAutocontrast(p=0.2),
            transforms.RandomEqualize(p=0.2),
        ])
        
        # Geometric transforms (applied consistently to both images)
        self.max_shift = 10  # Maximum pixel shift for geometric transforms
        
    def __call__(self, 
                left_img: np.ndarray, 
                right_img: np.ndarray,
                annotations: Dict[str, Any]) -> Tuple[np.ndarray, np.ndarray, Dict[str, Any]]:
        """
        Apply augmentations to a stereo pair and its annotations.
        
        Returns:
            Tuple of (augmented_left, augmented_right, updated_annotations)
        """
        # Convert to PIL Images for augmentation
        left_pil = F.to_pil_image(left_img)
        right_pil = F.to_pil_image(right_img)
        
        # Apply photometric transforms (independently)
        if self.use_color_jitter and random.random() > 0.5:
            left_pil = self.photometric(left_pil)
            right_pil = self.photometric(right_pil)
        
        # Apply geometric transforms (consistently to both images)
        if self.use_geometric and random.random() > 0.5:
            left_pil, right_pil, annotations = self._apply_geometric(
                left_pil, right_pil, annotations)
        
        # Apply cutout
        if self.use_cutout and random.random() > 0.5:
            left_pil = self._apply_cutout(left_pil)
            right_pil = self._apply_cutout(right_pil)
        
        # Convert back to numpy arrays
        left_img = np.array(left_pil)
        right_img = np.array(right_pil)
        
        return left_img, right_img, annotations
    
    def _apply_geometric(self, 
                        left_img, 
                        right_img, 
                        annotations: Dict[str, Any]) -> Tuple[Any, Any, Dict[str, Any]]:
        """Apply consistent geometric transformations to both images."""
        # Random horizontal shift (simulates small camera movement)
        h_shift = random.randint(-self.max_shift, self.max_shift)
        v_shift = random.randint(-self.max_shift // 2, self.max_shift // 2)
        
        # Apply shift
        left_img = F.affine(
            left_img, 
            angle=0, 
            translate=(h_shift, v_shift), 
            scale=1.0, 
            shear=0
        )
        right_img = F.affine(
            right_img, 
            angle=0, 
            translate=(h_shift, v_shift), 
            scale=1.0, 
            shear=0
        )
        
        # Random rotation (small angles only to preserve stereo geometry)
        if random.random() > 0.7:
            angle = random.uniform(-5, 5)
            left_img = F.rotate(left_img, angle)
            right_img = F.rotate(right_img, angle)
        
        # Random scaling (slight zoom)
        if random.random() > 0.7:
            scale = random.uniform(0.9, 1.1)
            new_size = [int(s * scale) for s in left_img.size[::-1]]
            left_img = F.resize(left_img, new_size)
            right_img = F.resize(right_img, new_size)
            
            # Center crop back to original size
            left_img = F.center_crop(left_img, self.image_size)
            right_img = F.center_crop(right_img, self.image_size)
        
        # Update annotations if needed (e.g., bounding boxes)
        # This is a simplified example - you'd need to adjust based on your annotation format
        if 'bounding_boxes' in annotations:
            for box in annotations['bounding_boxes']:
                # Apply same transformations to bounding box coordinates
                box[0] += h_shift  # x1
                box[1] += v_shift  # y1
                box[2] += h_shift  # x2
                box[3] += v_shift  # y2
        
        return left_img, right_img, annotations
    
    def _apply_cutout(self, img, n_holes: int = 3, length: int = 16) -> Any:
        """Randomly mask out square regions of the image."""
        h = img.size[1]
        w = img.size[0]
        
        for _ in range(n_holes):
            y = np.random.randint(h)
            x = np.random.randint(w)
            
            y1 = np.clip(y - length // 2, 0, h)
            y2 = np.clip(y + length // 2, 0, h)
            x1 = np.clip(x - length // 2, 0, w)
            x2 = np.clip(x + length // 2, 0, w)
            
            # Create a black patch
            img = F.to_tensor(img)
            img[..., y1:y2, x1:x2] = 0
            img = F.to_pil_image(img)
            
        return img


class StereoMixup:
    """Mixup augmentation for stereo pairs."""
    
    def __init__(self, alpha: float = 0.4):
        """
        Initialize Mixup.
        
        Args:
            alpha: Mixup alpha parameter (controls interpolation strength)
        """
        self.alpha = alpha
    
    def __call__(self, 
                left1: np.ndarray, right1: np.ndarray, targets1: Dict,
                left2: np.ndarray, right2: np.ndarray, targets2: Dict) -> Tuple:
        """
        Apply mixup between two samples.
        
        Returns:
            Mixed left image, mixed right image, mixed targets
        """
        # Sample mixup ratio
        lam = np.random.beta(self.alpha, self.alpha)
        
        # Mix images
        left_mix = left1 * lam + left2 * (1 - lam)
        right_mix = right1 * lam + right2 * (1 - lam)
        
        # Mix targets (weighted average)
        targets_mix = {}
        for k in targets1:
            if isinstance(targets1[k], (int, float)) and k in targets2:
                targets_mix[k] = targets1[k] * lam + targets2[k] * (1 - lam)
            else:
                targets_mix[k] = targets1[k]
        
        return left_mix, right_mix, targets_mix


def get_augmentation_pipeline(mode: str = 'train', **kwargs) -> StereoAugmentation:
    """Get augmentation pipeline for training or validation."""
    if mode == 'train':
        return StereoAugmentation(
            use_color_jitter=True,
            use_geometric=True,
            use_cutout=True,
            **kwargs
        )
    else:
        # For validation, only apply minimal augmentations
        return StereoAugmentation(
            use_color_jitter=False,
            use_geometric=False,
            use_cutout=False,
            **kwargs
        )
