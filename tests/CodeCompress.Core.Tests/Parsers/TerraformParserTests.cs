using System.Text;
using CodeCompress.Core.Models;
using CodeCompress.Core.Parsers;

namespace CodeCompress.Core.Tests.Parsers;

internal sealed class TerraformParserTests
{
    private readonly TerraformParser _parser = new();

    private ParseResult Parse(string source, string filePath = "main.tf")
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return _parser.Parse(filePath, bytes);
    }

    // ── Interface contract ──────────────────────────────────────

    [Test]
    public async Task LanguageIdIsTerraform()
    {
        await Assert.That(_parser.LanguageId).IsEqualTo("terraform");
    }

    [Test]
    public async Task FileExtensionsContainsTf()
    {
        await Assert.That(_parser.FileExtensions).Contains(".tf");
    }

    [Test]
    public async Task FileExtensionsContainsTfvars()
    {
        await Assert.That(_parser.FileExtensions).Contains(".tfvars");
    }

    [Test]
    public async Task ParserAutoDiscoveryFileExtensions()
    {
        await Assert.That(_parser.FileExtensions).Count().IsEqualTo(2);
        await Assert.That(_parser.FileExtensions).Contains(".tf");
        await Assert.That(_parser.FileExtensions).Contains(".tfvars");
    }

    // ── Empty / trivial files ───────────────────────────────────

    [Test]
    public async Task EmptyTfFileReturnsEmptyResult()
    {
        var result = Parse("");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task WhitespaceOnlyFileReturnsEmptyResult()
    {
        var result = Parse("   \n\n   \n");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Resource blocks ─────────────────────────────────────────

    [Test]
    public async Task ParseResourceBlock()
    {
        var source = """
            resource "aws_instance" "web" {
              ami           = "ami-12345"
              instance_type = "t3.micro"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("aws_instance.web");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(symbol.Signature).Contains("resource");
        await Assert.That(symbol.Signature).Contains("aws_instance");
        await Assert.That(symbol.Signature).Contains("web");
    }

    [Test]
    public async Task ResourceBlockSignatureIncludesFullDeclaration()
    {
        var source = """
            resource "aws_s3_bucket" "data_lake" {
              bucket = "my-data-lake"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Signature).Contains("resource \"aws_s3_bucket\" \"data_lake\"");
    }

    // ── Data source blocks ──────────────────────────────────────

    [Test]
    public async Task ParseDataSourceBlock()
    {
        var source = """
            data "aws_ami" "ubuntu" {
              most_recent = true
              owners      = ["099720109477"]
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("data.aws_ami.ubuntu");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Class);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);
    }

    // ── Variable blocks ─────────────────────────────────────────

    [Test]
    public async Task ParseVariableBlockWithDescriptionTypeAndDefault()
    {
        var source = """
            variable "instance_type" {
              description = "The EC2 instance type"
              type        = string
              default     = "t3.micro"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("var.instance_type");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(symbol.DocComment).IsEqualTo("The EC2 instance type");
        await Assert.That(symbol.Signature).Contains("type");
        await Assert.That(symbol.Signature).Contains("default");
    }

    [Test]
    public async Task ParseVariableWithNoDefault()
    {
        var source = """
            variable "region" {
              type = string
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("var.region");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(symbol.Signature).Contains("type");
    }

    [Test]
    public async Task ParseVariableWithNoType()
    {
        var source = """
            variable "enable_feature" {
              default = true
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("var.enable_feature");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(symbol.Signature).Contains("default");
    }

    [Test]
    public async Task ParseVariableWithComplexType()
    {
        var source = """
            variable "tags" {
              description = "Resource tags"
              type = map(string)
              default = {}
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("var.tags");
        await Assert.That(symbol.DocComment).IsEqualTo("Resource tags");
    }

    // ── Output blocks ───────────────────────────────────────────

    [Test]
    public async Task ParseOutputBlock()
    {
        var source = """
            output "instance_ip" {
              value = aws_instance.web.public_ip
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("output.instance_ip");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Export);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParseOutputBlockWithDescription()
    {
        var source = """
            output "instance_ip" {
              description = "The public IP of the instance"
              value       = aws_instance.web.public_ip
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.DocComment).IsEqualTo("The public IP of the instance");
    }

    // ── Locals blocks ───────────────────────────────────────────

    [Test]
    public async Task ParseLocalsBlockIndividualValues()
    {
        var source = """
            locals {
              common_tags = {
                Environment = "production"
                Project     = "myapp"
              }
              name_prefix = "myapp-prod"
            }
            """;

        var result = Parse(source);

        var commonTags = result.Symbols.FirstOrDefault(s => s.Name == "local.common_tags");
        await Assert.That(commonTags).IsNotNull();
        await Assert.That(commonTags!.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(commonTags.Visibility).IsEqualTo(Visibility.Private);

        var namePrefix = result.Symbols.FirstOrDefault(s => s.Name == "local.name_prefix");
        await Assert.That(namePrefix).IsNotNull();
        await Assert.That(namePrefix!.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(namePrefix.Visibility).IsEqualTo(Visibility.Private);
    }

    [Test]
    public async Task MultipleLocalsBlocksInSameFile()
    {
        var source = """
            locals {
              env = "staging"
            }

            locals {
              region = "us-east-1"
            }
            """;

        var result = Parse(source);

        var env = result.Symbols.FirstOrDefault(s => s.Name == "local.env");
        await Assert.That(env).IsNotNull();

        var region = result.Symbols.FirstOrDefault(s => s.Name == "local.region");
        await Assert.That(region).IsNotNull();
    }

    // ── Module blocks ───────────────────────────────────────────

    [Test]
    public async Task ParseModuleBlockWithLocalSource()
    {
        var source = """
            module "vpc" {
              source = "./modules/vpc"
              cidr   = "10.0.0.0/16"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("module.vpc");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Module);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("./modules/vpc");
    }

    [Test]
    public async Task ParseModuleBlockWithRegistrySource()
    {
        var source = """
            module "consul" {
              source  = "hashicorp/consul/aws"
              version = "0.1.0"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("module.consul");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Module);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("hashicorp/consul/aws");
    }

    [Test]
    public async Task ParseModuleBlockWithVersionAttribute()
    {
        var source = """
            module "eks" {
              source  = "terraform-aws-modules/eks/aws"
              version = "~> 19.0"

              cluster_name    = "my-cluster"
              cluster_version = "1.27"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("module.eks");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.Module);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("terraform-aws-modules/eks/aws");
    }

    [Test]
    public async Task ModuleBlockDependencyAliasIsModuleName()
    {
        var source = """
            module "networking" {
              source = "./modules/network"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies[0].Alias).IsEqualTo("networking");
    }

    // ── .tfvars files ───────────────────────────────────────────

    [Test]
    public async Task ParseTfvarsFileSimpleAssignment()
    {
        var source = """
            instance_type = "t3.large"
            """;

        var result = Parse(source, "terraform.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("instance_type");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.ConfigKey);
        await Assert.That(symbol.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(symbol.Signature).Contains("t3.large");
    }

    [Test]
    public async Task ParseTfvarsFileMultipleAssignments()
    {
        var source = """
            instance_type = "t3.large"
            region        = "us-west-2"
            enable_dns    = true
            """;

        var result = Parse(source, "prod.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(3);

        var instanceType = result.Symbols.First(s => s.Name == "instance_type");
        await Assert.That(instanceType.Kind).IsEqualTo(SymbolKind.ConfigKey);

        var region = result.Symbols.First(s => s.Name == "region");
        await Assert.That(region.Kind).IsEqualTo(SymbolKind.ConfigKey);

        var enableDns = result.Symbols.First(s => s.Name == "enable_dns");
        await Assert.That(enableDns.Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task ParseTfvarsFileWithMapValue()
    {
        var source = """
            tags = {
              Environment = "production"
              Team        = "platform"
            }
            """;

        var result = Parse(source, "terraform.tfvars");

        var symbol = result.Symbols.First(s => s.Name == "tags");
        await Assert.That(symbol.Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    // ── Comments ────────────────────────────────────────────────

    [Test]
    public async Task LineCommentPrecedingBlockCapturedAsDocComment()
    {
        var source = """
            # This is the main web server instance
            resource "aws_instance" "web" {
              ami           = "ami-12345"
              instance_type = "t3.micro"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.DocComment).IsEqualTo("This is the main web server instance");
    }

    [Test]
    public async Task BlockCommentPrecedingResourceCapturedAsDocComment()
    {
        var source = """
            /* This is the primary database instance
               used for production workloads */
            resource "aws_rds_instance" "primary" {
              engine = "postgres"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.DocComment).IsNotNull();
        await Assert.That(symbol.DocComment!).Contains("primary database instance");
    }

    [Test]
    public async Task MultipleLineCommentsPrecedingBlockCapturedAsDocComment()
    {
        var source = """
            # This is the web server
            # It runs in the public subnet
            resource "aws_instance" "web" {
              ami = "ami-12345"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.DocComment).IsNotNull();
        await Assert.That(symbol.DocComment!).Contains("web server");
    }

    [Test]
    public async Task CommentedOutBlockNotParsed()
    {
        var source = """
            # resource "aws_instance" "old" {
            #   ami = "ami-old"
            # }

            resource "aws_instance" "new" {
              ami = "ami-new"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.new");
    }

    [Test]
    public async Task BlockCommentedOutBlockNotParsed()
    {
        var source = """
            /*
            resource "aws_instance" "disabled" {
              ami = "ami-disabled"
            }
            */

            resource "aws_instance" "active" {
              ami = "ami-active"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.active");
    }

    // ── Dynamic blocks and complex HCL ──────────────────────────

    [Test]
    public async Task DynamicBlockInsideResourceDoesNotCreateExtraSymbol()
    {
        var source = """
            resource "aws_security_group" "main" {
              name = "main-sg"

              dynamic "ingress" {
                for_each = var.ingress_rules
                content {
                  from_port   = ingress.value.from_port
                  to_port     = ingress.value.to_port
                  protocol    = ingress.value.protocol
                  cidr_blocks = ingress.value.cidr_blocks
                }
              }
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_security_group.main");
    }

    [Test]
    public async Task NestedLifecycleBlockDoesNotCreateExtraSymbol()
    {
        var source = """
            resource "aws_instance" "web" {
              ami           = "ami-12345"
              instance_type = "t3.micro"

              lifecycle {
                create_before_destroy = true
                prevent_destroy       = false
              }
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    [Test]
    public async Task NestedProvisionerBlockDoesNotCreateExtraSymbol()
    {
        var source = """
            resource "aws_instance" "web" {
              ami           = "ami-12345"
              instance_type = "t3.micro"

              provisioner "remote-exec" {
                inline = [
                  "sudo apt-get update",
                  "sudo apt-get install -y nginx"
                ]
              }
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    // ── Heredoc handling ────────────────────────────────────────

    [Test]
    public async Task HeredocInsideResourceDoesNotConfuseBraceMatching()
    {
        var source = """
            resource "aws_iam_policy" "example" {
              name = "example-policy"
              policy = <<-EOF
                {
                  "Version": "2012-10-17",
                  "Statement": [
                    {
                      "Effect": "Allow",
                      "Action": "s3:GetObject",
                      "Resource": "*"
                    }
                  ]
                }
              EOF
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_iam_policy.example");
    }

    [Test]
    public async Task HeredocWithBracesAndResourceAfter()
    {
        var source = """
            resource "aws_iam_policy" "first" {
              policy = <<-EOF
                { "key": "value" }
              EOF
            }

            resource "aws_instance" "second" {
              ami = "ami-12345"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);
        var names = result.Symbols.Select(s => s.Name).ToList();
        await Assert.That(names).Contains("aws_iam_policy.first");
        await Assert.That(names).Contains("aws_instance.second");
    }

    // ── String literals with braces ─────────────────────────────

    [Test]
    public async Task StringLiteralsContainingBracesDoNotConfuseBraceMatching()
    {
        var source = """
            resource "aws_instance" "web" {
              ami           = "ami-12345"
              instance_type = "t3.micro"
              user_data     = "#!/bin/bash\necho '{\"key\": \"value\"}'"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    // ── Multiple blocks in single file ──────────────────────────

    [Test]
    public async Task MultipleBlocksInSingleFileCorrectCount()
    {
        var source = """
            resource "aws_instance" "web" {
              ami           = "ami-12345"
              instance_type = "t3.micro"
            }

            resource "aws_instance" "api" {
              ami           = "ami-12345"
              instance_type = "t3.small"
            }

            resource "aws_s3_bucket" "logs" {
              bucket = "my-logs"
            }

            variable "region" {
              type    = string
              default = "us-east-1"
            }

            variable "env" {
              type = string
            }

            output "web_ip" {
              value = aws_instance.web.public_ip
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(6);

        var resources = result.Symbols.Where(s => s.Kind == SymbolKind.Class).ToList();
        await Assert.That(resources).Count().IsEqualTo(3);

        var variables = result.Symbols.Where(s => s.Kind == SymbolKind.Constant).ToList();
        await Assert.That(variables).Count().IsEqualTo(2);

        var outputs = result.Symbols.Where(s => s.Kind == SymbolKind.Export).ToList();
        await Assert.That(outputs).Count().IsEqualTo(1);
    }

    // ── Malformed HCL ───────────────────────────────────────────

    [Test]
    public async Task MalformedHclUnclosedBracesDoesNotThrow()
    {
        var source = """
            resource "aws_instance" "web" {
              ami = "ami-12345"
            """;

        var result = Parse(source);

        // Should not throw; graceful degradation
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task MalformedHclExtraBracesDoesNotThrow()
    {
        var source = """
            resource "aws_instance" "web" {
              ami = "ami-12345"
            }}
            """;

        var result = Parse(source);

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task MalformedHclMissingBlockNameDoesNotThrow()
    {
        var source = """
            resource {
              ami = "ami-12345"
            }
            """;

        var result = Parse(source);

        await Assert.That(result).IsNotNull();
    }

    // ── ByteOffset and LineStart/LineEnd accuracy ───────────────

    [Test]
    public async Task ByteOffsetAndLineStartAccuracyForFirstBlock()
    {
        var source = "resource \"aws_instance\" \"web\" {\n  ami = \"ami-12345\"\n}\n";

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.ByteOffset).IsEqualTo(0);
        await Assert.That(symbol.LineStart).IsEqualTo(1);
        await Assert.That(symbol.LineEnd).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task ByteOffsetAndLineStartAccuracyForSecondBlock()
    {
        var source = "resource \"aws_instance\" \"first\" {\n  ami = \"ami-111\"\n}\n\nresource \"aws_instance\" \"second\" {\n  ami = \"ami-222\"\n}\n";

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(2);

        var first = result.Symbols.First(s => s.Name == "aws_instance.first");
        await Assert.That(first.LineStart).IsEqualTo(1);

        var second = result.Symbols.First(s => s.Name == "aws_instance.second");
        await Assert.That(second.LineStart).IsEqualTo(5);
        await Assert.That(second.ByteOffset).IsGreaterThan(0);
    }

    [Test]
    public async Task ByteLengthSpansEntireBlock()
    {
        var source = "resource \"aws_instance\" \"web\" {\n  ami = \"ami-12345\"\n}\n";

        var result = Parse(source);

        var symbol = result.Symbols[0];
        var blockText = source.Substring(symbol.ByteOffset, symbol.ByteLength);
        await Assert.That(blockText).Contains("resource");
        await Assert.That(blockText).Contains("}");
    }

    // ── Terraform and provider blocks ───────────────────────────

    [Test]
    public async Task ParseTerraformBlockWithRequiredProviders()
    {
        var source = """
            terraform {
              required_version = ">= 1.0"

              required_providers {
                aws = {
                  source  = "hashicorp/aws"
                  version = "~> 5.0"
                }
              }
            }
            """;

        var result = Parse(source);

        // terraform block should be captured as a symbol
        await Assert.That(result.Symbols).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ParseProviderBlock()
    {
        var source = """
            provider "aws" {
              region = "us-east-1"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsGreaterThanOrEqualTo(1);
    }

    // ── Full integration test ───────────────────────────────────

    [Test]
    public async Task FullIntegrationComplexFileWithAllBlockTypes()
    {
        var source = """
            # Main infrastructure configuration
            terraform {
              required_version = ">= 1.0"
            }

            provider "aws" {
              region = var.region
            }

            # The AWS region to deploy to
            variable "region" {
              description = "AWS region for deployment"
              type        = string
              default     = "us-east-1"
            }

            variable "instance_count" {
              description = "Number of instances"
              type        = number
              default     = 2
            }

            locals {
              common_tags = {
                Project = "myapp"
              }
              name_prefix = "myapp"
            }

            data "aws_ami" "ubuntu" {
              most_recent = true
              owners      = ["099720109477"]
            }

            resource "aws_instance" "web" {
              count         = var.instance_count
              ami           = data.aws_ami.ubuntu.id
              instance_type = "t3.micro"

              tags = local.common_tags
            }

            resource "aws_s3_bucket" "logs" {
              bucket = "${local.name_prefix}-logs"
            }

            module "vpc" {
              source = "./modules/vpc"
              cidr   = "10.0.0.0/16"
            }

            module "rds" {
              source  = "terraform-aws-modules/rds/aws"
              version = "~> 5.0"
            }

            output "instance_ids" {
              description = "IDs of the created instances"
              value       = aws_instance.web[*].id
            }

            output "bucket_arn" {
              value = aws_s3_bucket.logs.arn
            }
            """;

        var result = Parse(source);

        // Variables
        var regionVar = result.Symbols.First(s => s.Name == "var.region");
        await Assert.That(regionVar.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(regionVar.DocComment).IsEqualTo("AWS region for deployment");

        var countVar = result.Symbols.First(s => s.Name == "var.instance_count");
        await Assert.That(countVar.Kind).IsEqualTo(SymbolKind.Constant);

        // Locals
        var commonTags = result.Symbols.First(s => s.Name == "local.common_tags");
        await Assert.That(commonTags.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(commonTags.Visibility).IsEqualTo(Visibility.Private);

        var namePrefix = result.Symbols.First(s => s.Name == "local.name_prefix");
        await Assert.That(namePrefix.Visibility).IsEqualTo(Visibility.Private);

        // Data source
        var ami = result.Symbols.First(s => s.Name == "data.aws_ami.ubuntu");
        await Assert.That(ami.Kind).IsEqualTo(SymbolKind.Class);

        // Resources
        var webInstance = result.Symbols.First(s => s.Name == "aws_instance.web");
        await Assert.That(webInstance.Kind).IsEqualTo(SymbolKind.Class);

        var bucket = result.Symbols.First(s => s.Name == "aws_s3_bucket.logs");
        await Assert.That(bucket.Kind).IsEqualTo(SymbolKind.Class);

        // Modules
        var vpcModule = result.Symbols.First(s => s.Name == "module.vpc");
        await Assert.That(vpcModule.Kind).IsEqualTo(SymbolKind.Module);

        var rdsModule = result.Symbols.First(s => s.Name == "module.rds");
        await Assert.That(rdsModule.Kind).IsEqualTo(SymbolKind.Module);

        // Outputs
        var instanceIds = result.Symbols.First(s => s.Name == "output.instance_ids");
        await Assert.That(instanceIds.Kind).IsEqualTo(SymbolKind.Export);
        await Assert.That(instanceIds.DocComment).IsEqualTo("IDs of the created instances");

        var bucketArn = result.Symbols.First(s => s.Name == "output.bucket_arn");
        await Assert.That(bucketArn.Kind).IsEqualTo(SymbolKind.Export);

        // Dependencies from modules
        var vpcDep = result.Dependencies.First(d => d.RequirePath == "./modules/vpc");
        await Assert.That(vpcDep).IsNotNull();

        var rdsDep = result.Dependencies.First(d => d.RequirePath == "terraform-aws-modules/rds/aws");
        await Assert.That(rdsDep).IsNotNull();
    }

    // ── Edge cases ──────────────────────────────────────────────

    [Test]
    public async Task ResourceWithSingleLineBody()
    {
        var source = """
            resource "null_resource" "trigger" {}
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task VariableDescriptionWithQuotesInside()
    {
        var source = """
            variable "name" {
              description = "The \"display\" name"
              type        = string
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols[0];
        await Assert.That(symbol.Name).IsEqualTo("var.name");
    }

    [Test]
    public async Task CommentsOnlyFileReturnsEmptyResult()
    {
        var source = """
            # This file is intentionally left blank
            # TODO: Add resources later
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    [Test]
    public async Task InlineCommentsDoNotAffectParsing()
    {
        var source = """
            resource "aws_instance" "web" { # main server
              ami           = "ami-12345"    # base image
              instance_type = "t3.micro"     # smallest instance
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    [Test]
    public async Task SlashSlashCommentsHandled()
    {
        var source = """
            // This is a Terraform comment using double slashes
            resource "aws_instance" "web" {
              ami = "ami-12345"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
    }

    [Test]
    public async Task EmptyTfvarsFileReturnsEmptyResult()
    {
        var result = Parse("", "empty.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    [Test]
    public async Task TfvarsWithCommentsOnly()
    {
        var source = """
            # No values yet
            # TODO: fill in
            """;

        var result = Parse(source, "dev.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ResourceBlockParentSymbolIsNull()
    {
        var source = """
            resource "aws_instance" "web" {
              ami = "ami-12345"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols[0].ParentSymbol).IsNull();
    }

    [Test]
    public async Task LocalsParentSymbolIsNull()
    {
        var source = """
            locals {
              env = "prod"
            }
            """;

        var result = Parse(source);

        var symbol = result.Symbols.First(s => s.Name == "local.env");
        await Assert.That(symbol.ParentSymbol).IsNull();
    }

    [Test]
    public async Task ModuleWithGitSource()
    {
        var source = """
            module "custom" {
              source = "git::https://example.com/modules/vpc.git?ref=v1.0.0"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(1);
        await Assert.That(result.Dependencies[0].RequirePath).IsEqualTo("git::https://example.com/modules/vpc.git?ref=v1.0.0");
    }

    [Test]
    public async Task MultipleModulesCreateMultipleDependencies()
    {
        var source = """
            module "vpc" {
              source = "./modules/vpc"
            }

            module "ecs" {
              source = "./modules/ecs"
            }

            module "rds" {
              source = "terraform-aws-modules/rds/aws"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(3);
    }

    [Test]
    public async Task NoDependenciesWhenNoModules()
    {
        var source = """
            resource "aws_instance" "web" {
              ami = "ami-12345"
            }

            variable "region" {
              type = string
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Dependencies).Count().IsEqualTo(0);
    }

    // ── Parameterized tests ─────────────────────────────────────

    [Test]
    [Arguments("resource", "aws_instance", "web", "aws_instance.web", SymbolKind.Class)]
    [Arguments("resource", "aws_s3_bucket", "data", "aws_s3_bucket.data", SymbolKind.Class)]
    [Arguments("resource", "google_compute_instance", "vm", "google_compute_instance.vm", SymbolKind.Class)]
    public async Task ResourceBlockNamingConvention(
        string blockType, string resourceType, string resourceName,
        string expectedName, SymbolKind expectedKind)
    {
        var source = $"{blockType} \"{resourceType}\" \"{resourceName}\" {{\n  foo = \"bar\"\n}}\n";

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo(expectedName);
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(expectedKind);
    }

    [Test]
    [Arguments("variable", "var.region")]
    [Arguments("output", "output.ip")]
    public async Task SingleLabelBlockNaming(string blockType, string expectedName)
    {
        var label = expectedName.Split('.')[1];
        var source = $"{blockType} \"{label}\" {{\n  type = string\n}}\n";

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo(expectedName);
    }

    // ── Terraform-specific constructs ───────────────────────────

    [Test]
    public async Task ForExpressionInLocalDoesNotConfuseParsing()
    {
        var source = """
            locals {
              upper_names = [for name in var.names : upper(name)]
            }

            resource "aws_instance" "web" {
              ami = "ami-12345"
            }
            """;

        var result = Parse(source);

        var local = result.Symbols.FirstOrDefault(s => s.Name == "local.upper_names");
        await Assert.That(local).IsNotNull();

        var resource = result.Symbols.FirstOrDefault(s => s.Name == "aws_instance.web");
        await Assert.That(resource).IsNotNull();
    }

    [Test]
    public async Task TemplateInterpolationInStringDoesNotAffectParsing()
    {
        var source = """
            resource "aws_instance" "web" {
              ami  = "ami-12345"
              tags = {
                Name = "${var.env}-${var.name}"
              }
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    [Test]
    public async Task CountMetaArgumentDoesNotAffectParsing()
    {
        var source = """
            resource "aws_instance" "web" {
              count         = 3
              ami           = "ami-12345"
              instance_type = "t3.micro"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    [Test]
    public async Task ForEachMetaArgumentDoesNotAffectParsing()
    {
        var source = """
            resource "aws_instance" "web" {
              for_each      = toset(["a", "b", "c"])
              ami           = "ami-12345"
              instance_type = "t3.micro"
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("aws_instance.web");
    }

    [Test]
    public async Task VariableWithValidationBlock()
    {
        var source = """
            variable "instance_type" {
              description = "EC2 instance type"
              type        = string
              default     = "t3.micro"

              validation {
                condition     = contains(["t3.micro", "t3.small"], var.instance_type)
                error_message = "Invalid instance type."
              }
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("var.instance_type");
        await Assert.That(result.Symbols[0].DocComment).IsEqualTo("EC2 instance type");
    }

    [Test]
    public async Task OutputWithSensitiveFlag()
    {
        var source = """
            output "db_password" {
              description = "Database password"
              value       = aws_db_instance.main.password
              sensitive   = true
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("output.db_password");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Export);
    }

    [Test]
    public async Task DataSourceWithFilterBlocks()
    {
        var source = """
            data "aws_ami" "amazon_linux" {
              most_recent = true

              filter {
                name   = "name"
                values = ["amzn2-ami-hvm-*-x86_64-gp2"]
              }

              filter {
                name   = "virtualization-type"
                values = ["hvm"]
              }

              owners = ["amazon"]
            }
            """;

        var result = Parse(source);

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("data.aws_ami.amazon_linux");
    }

    [Test]
    public async Task TfvarsWithListValue()
    {
        var source = """
            availability_zones = ["us-east-1a", "us-east-1b", "us-east-1c"]
            """;

        var result = Parse(source, "terraform.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("availability_zones");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task TfvarsWithNumericValue()
    {
        var source = """
            instance_count = 5
            """;

        var result = Parse(source, "settings.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.ConfigKey);
    }

    [Test]
    public async Task TfvarsWithBooleanValue()
    {
        var source = """
            enable_monitoring = true
            """;

        var result = Parse(source, "flags.tfvars");

        await Assert.That(result.Symbols).Count().IsEqualTo(1);
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.ConfigKey);
    }
}
