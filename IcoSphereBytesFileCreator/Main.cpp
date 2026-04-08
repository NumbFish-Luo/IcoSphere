import <iostream>;
import IcoSphere.IcoSphere;

using std::cout;
using std::endl;

int main() {
    cout << "hello world" << endl;

    int recursion = 5;
    auto pack{ IcoSphere::IcoSphere::NewPackAndSave(recursion) };

    cout << "over!" << endl;
}
