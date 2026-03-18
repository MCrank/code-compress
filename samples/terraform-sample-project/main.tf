# Main infrastructure configuration for the web application
terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.5"
    }
  }

  backend "s3" {
    bucket = "terraform-state"
    key    = "prod/terraform.tfstate"
    region = "us-east-1"
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = local.common_tags
  }
}

# Common tags applied to all resources
locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }
  name_prefix = "${var.project_name}-${var.environment}"

  // Region-specific configuration using nested maps
  region_config = {
    "us-east-1" = {
      ami           = "ami-0c55b159cbfafe1f0"
      instance_type = "t3.medium"
      az_count      = 3
    }
    "eu-west-1" = {
      ami           = "ami-0d71ea30463e0ff8d"
      instance_type = "t3.small"
      az_count      = 2
    }
  }
}
