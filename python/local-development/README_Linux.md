
# Disclaimer
 
We highly recommend using the Docker environment in Linux because of the differences in distributions. To do this, look to the README_Docker.md.
 
# Python
Currently tested to work with [3.8.7](https://www.python.org/downloads/release/python-387/), but other 3.7 and 3.8 Python versions are expected to also work.

# Install and configure PythonNet dependencies - Ubuntu 20.04
- Install Mono version 6.10.0:
    ```
    sudo apt install gnupg ca-certificates
    sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
    echo "deb https://download.mono-project.com/repo/ubuntu stable-focal/snapshots/6.10.0 main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list && sudo apt-get update
    sudo apt update
    sudo apt install mono-devel
    ```  
    for other linux distributions, please see https://www.mono-project.com/download/stable/#download-lin and https://www.mono-project.com/docs/getting-started/install/linux/#accessing-older-releases

- Additional things to install:
    ```
    sudo apt install python3-pip clang libglib2.0-0 build-essential librdkafka-dev
    python3 -m pip install pycparser
    ```
    As of writing this document, some dependencies do no correctly install for librdkafka on ubuntu 20.04. These need to be manually installed with the following script, as not available pre-built.
    ```
    mkdir kafkadownloads
    cd kafkadownloads
    curl http://ftp.us.debian.org/debian/pool/main/g/glibc/multiarch-support_2.19-18+deb8u10_amd64.deb -O
    md5=$(md5sum multiarch-support_2.19-18+deb8u10_amd64.deb | cut -b 1-32)
    if [[ "$md5" != 'c3146eaa5ba5f757d1088b5217c2a313' ]]; then
    echo "Invalid Md5Hash for multiarch-support: $md5" 1>&2
    exit 1
    fi
    dpkg -i multiarch-support_2.19-18+deb8u10_amd64.deb

    cd ..
    rm -rdf kafkadownloads

    echo "deb http://security.ubuntu.com/ubuntu bionic-security main" >> /etc/apt/sources.list
    apt-get update
    apt-get install libssl1.0.0
    ```


# Create the new Python environment
We highly suggest using a Python virtual environment, because the Quix streaming package currently relies on some dll redirecting, which is achieved by adding a file to your Python environment. This is done automatically. You might want to rely on a similar techniques but, to avoid any complication with other Python applications, a virtual environment is advized.

To create a new virtual environment, execute the following in a terminal at your desired location:
```
python3 -m pip install virtualenv
python3 -m virtualenv env  --python=python3.8
chmod +x ./env/bin/activate
source ./env/bin/activate
```
You will know you succeeded in activating the environment if your terminal line starts with (env). Future steps will assume you have the virtual environment activated or are happy to install into global Python.

# Install samples requirements
In the same terminal that you activated the virtual environment, navigate to the folder where `requirements.txt` is located and execute
```
python3 -m pip install -r requirements.txt --extra-index-url https://pkgs.dev.azure.com/quix-analytics/53f7fe95-59fe-4307-b479-2473b96de6d1/_packaging/public/pypi/simple/
```

# Run the sample 
In the same terminal that you activated the virtual environment, navigate to the folder where `main.py` is located and execute
```
python3 main.py
```

# Known issues
#### 1. No such configuration property: "client.software.name".
This happens when an old version of librdkafka is picked up or some dependencies are not fulfilled. Please make sure you have followed "Additional things to install" section of this guide. If the issue still persists, please let us know.

When reporting issue to support@quix.ai, please include the result of following commands:
```
cat /etc/os-release
ldd ./env/lib/python3.8/site-packages/quixstreaming/dotnet/linux-x64/librdkafka.so
```

