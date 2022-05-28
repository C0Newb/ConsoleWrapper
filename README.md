# Console Wrapper

A tool to wrap STDIN, STDERR, and STDOUT on applications inorder to emulate user input on those programs.



---

The idea for this project is to wrap around a Minecraft (Java) server instance inorder to gracefully shutdown the Minecraft server with Windows.
Currently we are able to successfully capture STDIN, STDERR and STDOUT to read the console and input text. Issues arise when Windows goes to shutdown as it'll kill the Java instance before allowing us to gracefully shutdown.
