import requests
import json

url = "https://writer-{placeholder:workspaceId}.{placeholder:environment.subdomain}.quix.ai/topics/{placeholder:output}/streams/quick-start/parameters/data"

# send one timestamp with two parameters
payload = json.dumps({
  "timestamps": [
    1652956783
  ],
  "numericValues": {
    "temperature": [
      24.4
    ],
    "humidity": [
      61
    ]
  }
})
# configure the request headers
headers = {
  'Authorization': 'bearer {placeholder:token}',
  'Content-Type': 'application/json'
}

# send the POST request and get the response
response = requests.request("POST", url, headers=headers, data=payload)

# a response of 200 means the data was sent
print("{}:{}".format(response.status_code, response.text))