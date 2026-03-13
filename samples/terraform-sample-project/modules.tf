module "vpc" {
  source = "./modules/vpc"

  cidr_block  = "10.0.0.0/16"
  environment = var.environment
  name_prefix = local.name_prefix
}

module "monitoring" {
  source  = "terraform-aws-modules/cloudwatch/aws"
  version = "~> 4.0"

  dashboard_name = "${local.name_prefix}-dashboard"
}

module "s3_bucket" {
  source  = "terraform-aws-modules/s3-bucket/aws"
  version = "~> 3.15"

  bucket = "${local.name_prefix}-assets"
  acl    = "private"

  tags = local.common_tags
}
