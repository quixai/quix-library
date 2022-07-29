# Snowplow Connector
Connect your Snowplow data to a Quix topic with this bridge.

Note that this bridge works with Snowplow instances on Amazon Kinesis, so you will need access details for the Kinesis streams.

## Requirements/prerequisites

You will need Snowplow deployed to AWS to use this project.

## Environment variables

The code sample uses the following environment variables:

- **output**: This is the Quix Topic that will receive the stream.
- **aws_access_key_id**: AWS Access Key Id.
- **aws_secret_access_key**: AWS Secret Access Key.
- **aws_region_name**: AWS Region Name.
- **aws_stream_name**: The name of the AWS stream you want to use.   

## Docs

Check out the [SDK docs](https://quix.ai/docs/sdk/introduction.html) for detailed usage guidance.

## How to run
Create a [Quix](https://portal.platform.quix.ai/self-sign-up?xlink=github) account to edit or deploy this application without a local environment setup.

Alternatively, you can learn how to set up your local environment [here](https://quix.ai/docs/sdk/python-setup.html).
