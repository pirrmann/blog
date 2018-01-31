# My blog

Uses [Jekyll](https://jekyllrb.com) to generate [blog.vbfox.net](https://blog.vbfox.net).

## Building locally

* Install ruby
* Install python 2 (Ensure that it's in path)
* `bundle`
* `dev.cmd`

## Building in Docker

Build and run (Will be accessible on [http://127.0.0.1:8080](http://127.0.0.1:8080))

```bash
docker build . -t vbfox/blog
docker run --name vbfox-blog -it --rm -p 127.0.0.1:8080:80 vbfox/blog
# Ctrl+C to kill the container
```

## Running in Kubernetes

```bash
docker push vbfox/blog
kubectl create -f blog-k8s.yaml

# Wait for IP to appear
kubectl get service vbfox-blog --watch
```