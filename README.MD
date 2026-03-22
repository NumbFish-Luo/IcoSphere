# IcoSphere

## 一、简介

使用大量三角形组成球体，参考：[catch 22 - Andreas Kahler's blog: Creating an icosphere mesh in code](http://blog.andreaskahler.com/2009/06/creating-icosphere-mesh-in-code.html)

## 二、配置

需要打开深度图渲染选项，默认的UPR工程里面有一个叫URP-HighFidelity的渲染管线配置文件，勾选其Rendering/Depth Texture选项即可

## 三、Todo

按优先级从高到低排列：

- 拆分为单独的渲染实体，而非一个面数非常多的球体
- 使用Unity.Burst库对数据运算进行SIMD优化