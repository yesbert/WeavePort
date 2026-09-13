FROM weaveport-poc-python:1
COPY plugins/security/fixture.py /plugin/fixture.py
ARG READY_PROTOCOL=1
ENV WEAVEPORT_TEST_READY_PROTOCOL=$READY_PROTOCOL
ENTRYPOINT ["python", "-u", "/plugin/fixture.py"]
