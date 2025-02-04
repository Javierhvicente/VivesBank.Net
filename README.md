# VivesBank.Net

## Introducción de la API

En esta práctica, hemos desarrollado un servicio bancario con un enfoque modular y bien organizado. El sistema permite registrar clientes, cuentas, tarjetas y movimientos, y está diseñado para ser fácil de mantener y ampliar. Usamos el patrón Repository para separar la lógica de negocio del acceso a datos, y aplicamos principios de diseño como SOLID para asegurarnos de que el código sea limpio y fácil de entender.

Además, la comunicación entre el cliente y el servidor se hará a través de una API REST, que facilita la integración con otras aplicaciones. También implementamos GraphQL para ofrecer flexibilidad en las consultas de movimientos.

En resumen, el servicio bancario está diseñado para ser escalable y robusto, utilizando buenas prácticas de programación para crear un sistema seguro y eficiente.


## **Tecnologías y Enfoques Utilizados**

- Arquitectura orientada al dominio.
- Manejo de excepciones específico para el dominio.
- Uso de inyección de dependencias.

## **Base de Datos**

Este proyecto utiliza dos bases de datos para gestionar la información de usuarios y movimientos:

- **Base de datos remota (PostgreSQL con Docker):** Se encarga de gestionar los usuarios. La conexión se establece a través de un archivo _dockerCompose.yml_.
- **Base de datos remota (MongoDB con Docker):** Se encarga de gestionar los movimientos de los clientes. La conexión se configura en el archivo _appsettings.json_ y se utiliza un archivo _dockerCompose.yml_ para su despliegue.

## **Importación y Exportación de Datos**

El sistema permite la importación y exportación de datos para facilitar la gestión de la información de clientes y movimientos en los siguientes formatos:

- **PDF**
- **JSON**
- **CSV**

Además, se puede realizar una copia de seguridad en formato **ZIP**.

## **Lenguajes y Tecnologías**

- **C#**
- **.NET Core / ASP.NET Core**
- **HotChocolate**
- **Docker**
- **PostgreSQL**
- **MongoDB**
- **Postman**
- **ReFit (para llamadas a APIs)**
- **Git**
- **GitFlow**
- **NuGet**
- **Serilog (para logging)**
- **Swagger**
- **NUnit**
- **Moq**
- **Testcontainers.MongoDb**
- **Testcontainers.PostgreSql**

## **Calidad y Pruebas**

El proyecto implementa diversas prácticas y herramientas para asegurar la calidad y el correcto funcionamiento del código. A continuación se describen los principales enfoques utilizados:

- **Test Containers**
- **Pruebas Unitarias**
- **Pruebas de Integración**
- **Moq**
- **NUnit**
- **Cobertura de Código**

## Enlace al video

[Explicación del proyecto](https://www.youtube.com/watch?v=fG5jdisKxM8)

## Autores del programa

<table align="center">
  <tr>
    <td align="center">
      <a href="https://github.com/Javierhvicente">
        <img src="https://github.com/Javierhvicente.png" width="70" height="70" style="border-radius: 50%;" alt="Germán Fernández Carracedo"/>
        <br/>
        <sub><b>Javier</b></sub>
      </a>
    </td>
    <td align="center">
      <a href="https://github.com/Samuceese">
        <img src="https://github.com/Samuceese.png" width="70" height="70" style="border-radius: 50%;" alt="Samuel Cortés Sánchez"/>
        <br/>
        <sub><b>Samuel</b></sub>
      </a>
    </td>
        <td align="center">
      <a href="https://github.com/rraul10">
        <img src="https://github.com/rraul10.png" width="70" height="70" style="border-radius: 50%;" alt="Raúl Fernández"/>
        <br/>
        <sub><b>Raúl</b></sub>
      </a>
    </td>
    <td align="center">
      <a href="https://github.com/alvarito304">
        <img src="https://avatars.githubusercontent.com/u/114983881?v=4" width="70" height="70" style="border-radius: 50%;" alt="Álvaro Herrero Tamayo"/>
        <br/>
        <sub><b>Álvaro</b></sub>
      </a>
    </td>
        </td>
    <td align="center">
      <a href="https://github.com/germangfc">
        <img src="https://avatars.githubusercontent.com/u/147338370" width="70" height="70" style="border-radius: 50%;" alt="German"/>
        <br/>
        <sub><b>German</b></sub>
      </a>
      </td>
      <td align="center">
      <a href="https://github.com/diegonovi">
        <img src="https://github.com/diegonovi.png" width="70" height="70" style="border-radius: 50%;" alt="Diego Novillo"/>
        <br/>
        <sub><b>Diego</b></sub>
      </a>
      </td>
    <td align="center">
      <a href="https://github.com/TomasVaquerin">
        <img src="https://github.com/TomasVaquerin.png" width="70" height="70" style="border-radius: 50%;" alt="Tomas Vaquerin"/>
        <br/>
        <sub><b>Tomás</b></sub>
      </a>
      </td>
  </tr>
</table>
