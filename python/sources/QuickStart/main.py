from datetime import datetime
import requests
import json
import time
import random

url = "https://writer-{placeholder:workspaceId}.{placeholder:environment.subdomain}.quix.ai/topics/{placeholder:output}/streams/quick-start/parameters/data"
token = "bearer {placeholder:token}"


while True:
    date = datetime.utcnow()
    utc_time = time.time_ns()
    temperature = random.randint(0, 50)
    humidity = random.randint(0, 50)
    print("Timestamp:{} Temp:{} Humidity:{}".format(utc_time, temperature, humidity))

    # send one timestamp with two parameters
    payload = json.dumps({
    "timestamps": [
        utc_time
    ],
    "numericValues": {
        "temperature": [
        temperature
        ],
        "humidity": [
        humidity
        ]
    }
    })
    # configure the request headers
    headers = {
    'Authorization': token,
    'Content-Type': 'application/json'
    }

    # send the POST request and get the response
    response = requests.request("POST", url, headers=headers, data=payload)

    # a response of 200 means the data was sent
    print("Response is {}".format(response.status_code))

    time.sleep(1)