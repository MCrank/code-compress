module "vpc" {
  source = "./modules/vpc"

  cidr_block  = "10.0.0.0/16"
  environment = var.environment
  name_prefix = local.name_prefix
}

/*
 * Monitoring module for CloudWatch alarms and dashboards.
 * Uses the official AWS CloudWatch module from the Terraform registry.
 */
module "monitoring" {
  source  = "terraform-aws-modules/cloudwatch/aws/modules/metric-alarm"
  version = "~> 4.0"

  alarm_name          = "${local.name_prefix}-cpu-alarm"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "CPUUtilization"
  namespace           = "AWS/EC2"
  period              = 300
  statistic           = "Average"
  threshold           = 80

  tags = local.common_tags
}

module "s3_bucket" {
  source  = "terraform-aws-modules/s3-bucket/aws"
  version = "~> 3.15"

  bucket = "${local.name_prefix}-assets"
  acl    = "private"

  tags = local.common_tags
}
